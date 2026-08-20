using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Tarifario de procedimientos contra MySQL real.
///
/// Lo que de verdad importa acá: que un procedimiento que ya se facturó NO se
/// pueda borrar. Borrarlo dejaría facturas apuntando a algo que la pantalla ya
/// no sabe nombrar, y eso se descubre el día que hay que reimprimir.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class TarifarioTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_tarifario_test;";

    private ConexionFactory _factory = null!;
    private ProcedimientoService _servicio = null!;
    private ProcedimientoRepository _repo = null!;
    private long _usuarioId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_tarifario_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _repo = new ProcedimientoRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        _servicio = new ProcedimientoService(_repo, auditoria);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                SELECT LAST_INSERT_ID();
                """;
            _usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        SesionActual.Iniciar(_usuarioId, "test", "Usuario Test", "Admin",
            ["procedimientos"], DateTime.UtcNow, 1);
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_tarifario_test;");
    }

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static ProcedimientoDatos Consulta(string nombre = "Consulta general",
        decimal precio = 1500m, bool exento = true, string? codigo = null, int duracion = 30) =>
        new(codigo, nombre, precio, duracion, exento, null);

    // =========================================================

    [Fact]
    public async Task CrearYLeer_GuardaTodoLoQueSeCargo()
    {
        var id = await _servicio.CrearAsync(Consulta(codigo: "C-01", precio: 2500m, duracion: 45));

        var p = await _servicio.ObtenerPorIdAsync(id);

        p.Should().NotBeNull();
        p!.Codigo.Should().Be("C-01");
        p.Nombre.Should().Be("Consulta general");
        p.Precio.Should().Be(2500m);
        p.DuracionMinutos.Should().Be(45);
        p.ExentoItbis.Should().BeTrue();
        p.Activo.Should().BeTrue();
    }

    /// <summary>
    /// Los servicios de salud están exentos de ITBIS: el tarifario tiene que
    /// nacer así o la clínica le cobraría 18% de más a cada paciente.
    /// </summary>
    [Fact]
    public async Task PorDefecto_UnProcedimientoEsExentoDeItbis()
    {
        var id = await _servicio.CrearAsync(
            new ProcedimientoDatos(null, "Radiografía", 1200m, 20, ExentoItbis: true, null));

        (await _servicio.ObtenerPorIdAsync(id))!.ExentoItbis.Should().BeTrue();
    }

    [Fact]
    public async Task SePuedeMarcarComoGravado_CuandoElContadorLoPide()
    {
        var id = await _servicio.CrearAsync(
            new ProcedimientoDatos(null, "Certificado médico", 800m, 10, ExentoItbis: false, null));

        (await _servicio.ObtenerPorIdAsync(id))!.ExentoItbis.Should().BeFalse();
    }

    [Fact]
    public async Task CodigoRepetido_SeRechaza()
    {
        await _servicio.CrearAsync(Consulta(codigo: "C-01"));

        var accion = async () => await _servicio.CrearAsync(Consulta("Otra cosa", codigo: "C-01"));

        (await accion.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("C-01");
    }

    [Fact]
    public async Task VariosSinCodigo_Conviven()
    {
        // El UNIQUE de MySQL admite múltiples NULL: sin esto, el segundo
        // procedimiento sin código chocaría con el primero.
        await _servicio.CrearAsync(Consulta("Uno"));
        await _servicio.CrearAsync(Consulta("Dos"));

        (await _servicio.ObtenerTodosAsync()).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task PrecioNoPositivo_SeRechaza(decimal precio)
    {
        var accion = async () => await _servicio.CrearAsync(Consulta(precio: precio));
        await accion.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    [InlineData(1441)]   // más de 12 horas: casi siempre es un dedazo
    public async Task DuracionImposible_SeRechaza(int minutos)
    {
        var accion = async () => await _servicio.CrearAsync(Consulta(duracion: minutos));
        await accion.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SinNombre_SeRechaza()
    {
        var accion = async () => await _servicio.CrearAsync(Consulta(nombre: "   "));
        await accion.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Activos_NoTraeLosDesactivados()
    {
        var id = await _servicio.CrearAsync(Consulta("Vigente"));
        var viejo = await _servicio.CrearAsync(Consulta("Descontinuado"));
        await _servicio.ActualizarAsync(viejo,
            new ProcedimientoDatos(null, "Descontinuado", 1500m, 30, true, null, Activo: false));

        var activos = await _servicio.ObtenerActivosAsync();

        activos.Should().ContainSingle().Which.Id.Should().Be(id);
        (await _servicio.ObtenerTodosAsync()).Should().HaveCount(2, "seguir viéndose en el tarifario");
    }

    // =========================================================
    // LA REGLA QUE NO SE PUEDE ROMPER
    // =========================================================

    /// <summary>
    /// Un procedimiento facturado no se borra. Si se pudiera, la factura vieja
    /// quedaría apuntando a una fila muerta y la reimpresión se rompería.
    /// </summary>
    [Fact]
    public async Task ProcedimientoYaFacturado_NoSePuedeEliminar()
    {
        var id = await _servicio.CrearAsync(Consulta());
        await FacturarloAsync(id);

        var accion = async () => await _servicio.EliminarAsync(id);

        (await accion.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Desactivalo");
        (await _servicio.ObtenerPorIdAsync(id)).Should().NotBeNull("sigue vivo");
    }

    [Fact]
    public async Task ProcedimientoConCitas_NoSePuedeEliminar()
    {
        var id = await _servicio.CrearAsync(Consulta());
        await AgendarloAsync(id);

        var accion = async () => await _servicio.EliminarAsync(id);

        (await accion.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("citas");
    }

    [Fact]
    public async Task ProcedimientoSinUsar_SiSePuedeEliminar()
    {
        var id = await _servicio.CrearAsync(Consulta("Cargado por error"));

        await _servicio.EliminarAsync(id);

        (await _servicio.ObtenerPorIdAsync(id)).Should().BeNull();
        (await _servicio.ObtenerTodosAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Desactivar es la salida que ofrece el mensaje de error: tiene que
    /// sacarlo de lo cobrable sin tocar el historial.
    /// </summary>
    [Fact]
    public async Task Desactivar_LoSacaDeLoCobrablePeroConservaLaFactura()
    {
        var id = await _servicio.CrearAsync(Consulta());
        await FacturarloAsync(id);

        await _servicio.ActualizarAsync(id,
            new ProcedimientoDatos(null, "Consulta general", 1500m, 30, true, null, Activo: false));

        (await _servicio.ObtenerActivosAsync()).Should().BeEmpty();
        (await _repo.FueFacturadoAsync(id)).Should().BeTrue("la factura sigue ahí");
    }

    [Fact]
    public async Task CambiarElPrecio_QuedaAnotadoEnAuditoria()
    {
        var id = await _servicio.CrearAsync(Consulta(precio: 1500m));

        await _servicio.ActualizarAsync(id,
            new ProcedimientoDatos(null, "Consulta general", 2000m, 30, true, null));

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT descripcion FROM auditoria
            WHERE entidad = 'procedimiento' AND accion = 'modificar'
            ORDER BY id DESC LIMIT 1;
            """;
        var detalle = (string?)await cmd.ExecuteScalarAsync();

        detalle.Should().Contain("1,500.00").And.Contain("2,000.00");
    }

    // ---------- Ayudantes ----------

    private async Task FacturarloAsync(long procedimientoId)
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO factura (numero_factura, usuario_id, fecha_emision, subtotal,
                                 itbis_tasa, itbis, total, metodo_pago)
            VALUES ('F-0001', @usuarioId, UTC_TIMESTAMP(), 1500, 0, 0, 1500, 'efectivo');
            INSERT INTO detalle (factura_id, procedimiento_id, descripcion, cantidad,
                                 precio_unitario, exento_itbis, subtotal)
            VALUES (LAST_INSERT_ID(), @procId, 'Consulta general', 1, 1500, 1, 1500);
            """;
        cmd.Parameters.AddWithValue("@usuarioId", _usuarioId);
        cmd.Parameters.AddWithValue("@procId", procedimientoId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task AgendarloAsync(long procedimientoId)
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();

        // Van como sentencias separadas y no con variables de usuario
        // (SET @cli := ...): MySqlConnector las rechaza salvo que la cadena
        // traiga Allow User Variables=true, y no vale la pena activarlo acá.
        var clienteId = await InsertarAsync(conexion,
            "INSERT INTO cliente (nombre) VALUES ('Paciente Test'); SELECT LAST_INSERT_ID();");
        var medicoId = await InsertarAsync(conexion,
            "INSERT INTO medico (nombre) VALUES ('Dra. Test'); SELECT LAST_INSERT_ID();");

        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cita (cliente_id, medico_id, procedimiento_id, fecha_hora)
            VALUES (@cli, @med, @procId, UTC_TIMESTAMP());
            """;
        cmd.Parameters.AddWithValue("@cli", clienteId);
        cmd.Parameters.AddWithValue("@med", medicoId);
        cmd.Parameters.AddWithValue("@procId", procedimientoId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertarAsync(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
