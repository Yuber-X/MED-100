using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Consentimientos informados (015). Pedido de la clínica del 2026-09-21.
///
/// Lo que se verifica acá no es el CRUD: es que la lista que ve quien va a
/// imprimir sea la correcta (el del procedimiento MÁS los generales), que un
/// texto desactivado no se pueda poner a firmar, y que cada impresión deje
/// rastro en auditoría. Eso último es lo único que el sistema puede decir
/// después sobre un papel que se firmó a mano.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class ConsentimientosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_consentimientos_test;";

    private ConexionFactory _factory = null!;
    private ConsentimientoService _consentimientos = null!;

    private long _usuarioId;
    private long _procedimientoId;
    private long _otroProcedimientoId;
    private Cliente _paciente = null!;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_consentimientos_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();
        await new VerificadorBaseDatos(CadenaTest).ActualizarEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        _consentimientos = new ConsentimientoService(new ConsentimientoRepository(_factory), auditoria);

        long pacienteId;
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            pacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre, cedula) VALUES ('Paciente Uno', '402-0000000-1');
                """);
            _procedimientoId = await InsertarAsync(conexion, """
                INSERT INTO procedimiento (nombre, precio, duracion_minutos, exento_itbis)
                VALUES ('Extracción simple', 2500.00, 30, 1);
                """);
            _otroProcedimientoId = await InsertarAsync(conexion, """
                INSERT INTO procedimiento (nombre, precio, duracion_minutos, exento_itbis)
                VALUES ('Limpieza', 1500.00, 30, 1);
                """);
        }

        _paciente = new Cliente { Id = pacienteId, Nombre = "Paciente Uno", Cedula = "402-0000000-1" };
        EntrarComo("Admin", "procedimientos");
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_consentimientos_test;");
    }

    // =========================================================

    [Fact]
    public async Task ElParcheDeApertura_SiembraElConsentimientoGeneral()
    {
        // Una clínica que abre la app por primera vez tiene que encontrar algo
        // para imprimir, no una lista vacía.
        var lista = await _consentimientos.ObtenerTodosAsync();

        lista.Should().ContainSingle();
        lista[0].Titulo.Should().Be("Consentimiento informado general");
        lista[0].ProcedimientoId.Should().BeNull("el de ejemplo sirve para cualquier procedimiento");
        lista[0].Cuerpo.Should().Contain("Ley 172-13");
    }

    [Fact]
    public async Task ParaUnProcedimiento_TraeElSuyoYLosGenerales_PeroNoLosDeOtro()
    {
        await _consentimientos.CrearAsync(new ConsentimientoDatos(
            _procedimientoId, "Consentimiento para extracción", "Texto de la extracción."));
        await _consentimientos.CrearAsync(new ConsentimientoDatos(
            _otroProcedimientoId, "Consentimiento para limpieza", "Texto de la limpieza."));

        var lista = await _consentimientos.ObtenerParaProcedimientoAsync(_procedimientoId);

        lista.Select(c => c.Titulo).Should().BeEquivalentTo(
            ["Consentimiento informado general", "Consentimiento para extracción"]);
        lista[0].ProcedimientoId.Should().Be(_procedimientoId,
            "primero el del procedimiento, que es el que corresponde firmar");
    }

    [Fact]
    public async Task Desactivado_NoApareceParaImprimir()
    {
        var id = await _consentimientos.CrearAsync(new ConsentimientoDatos(
            _procedimientoId, "Consentimiento viejo", "Texto que ya no se usa."));

        await _consentimientos.ActualizarAsync(id, new ConsentimientoDatos(
            _procedimientoId, "Consentimiento viejo", "Texto que ya no se usa.", Activo: false));

        var paraImprimir = await _consentimientos.ObtenerParaProcedimientoAsync(_procedimientoId);
        paraImprimir.Should().NotContain(c => c.Id == id);

        var catalogo = await _consentimientos.ObtenerTodosAsync();
        catalogo.Should().Contain(c => c.Id == id, "en el catálogo sigue, para poder reactivarlo");
    }

    [Fact]
    public async Task SinTexto_SeNiega()
    {
        await FluentActions
            .Awaiting(() => _consentimientos.CrearAsync(
                new ConsentimientoDatos(null, "Sin cuerpo", "   ")))
            .Should().ThrowAsync<ArgumentException>();

        await FluentActions
            .Awaiting(() => _consentimientos.CrearAsync(
                new ConsentimientoDatos(null, "  ", "Con cuerpo pero sin título.")))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SinPermisoDeProcedimientos_NoSePuedeEditar()
    {
        // El Cajero imprime consentimientos todo el día; lo que no hace es
        // reescribir lo que el paciente firma.
        EntrarComo("Cajero", "vender", "clientes");

        _consentimientos.PuedeEditar.Should().BeFalse();

        await FluentActions
            .Awaiting(() => _consentimientos.CrearAsync(
                new ConsentimientoDatos(null, "Mío", "Texto.")))
            .Should().ThrowAsync<UnauthorizedAccessException>();

        var lista = await _consentimientos.ObtenerTodosAsync();
        lista.Should().NotBeEmpty("leerlos e imprimirlos sí puede");
    }

    [Fact]
    public async Task Imprimir_ArmaLaHojaYDejaRastroEnAuditoria()
    {
        var id = await _consentimientos.CrearAsync(new ConsentimientoDatos(
            _procedimientoId, "Consentimiento para extracción", "Texto de la extracción."));

        var hoja = await _consentimientos.PrepararImpresionAsync(id, _paciente, "Dra. Prueba");

        hoja.Titulo.Should().Be("Consentimiento para extracción");
        hoja.ProcedimientoNombre.Should().Be("Extracción simple");
        hoja.PacienteNombre.Should().Be("Paciente Uno");
        hoja.PacienteCedula.Should().Be("402-0000000-1");
        hoja.MedicoNombre.Should().Be("Dra. Prueba");

        var descripcion = await EscalarAsync("""
            SELECT descripcion FROM auditoria
            WHERE entidad = 'consentimiento' AND accion = 'consultar'
            ORDER BY id DESC LIMIT 1;
            """);
        descripcion.Should().NotBeNull();
        descripcion.Should().Contain("Paciente Uno");
        descripcion.Should().Contain("Extracción simple");
    }

    [Fact]
    public async Task DarDeBaja_LoSacaDeLaListaYQuedaEnAuditoria()
    {
        var id = await _consentimientos.CrearAsync(new ConsentimientoDatos(
            null, "Consentimiento de prueba", "Texto."));

        await _consentimientos.EliminarAsync(id);

        var lista = await _consentimientos.ObtenerTodosAsync();
        lista.Should().NotContain(c => c.Id == id);

        var descripcion = await EscalarAsync("""
            SELECT descripcion FROM auditoria
            WHERE entidad = 'consentimiento' AND accion = 'eliminar'
            ORDER BY id DESC LIMIT 1;
            """);
        descripcion.Should().Contain("Consentimiento de prueba");
    }

    [Fact]
    public async Task Imprimir_UnoDadoDeBaja_SeNiega()
    {
        var id = await _consentimientos.CrearAsync(new ConsentimientoDatos(
            null, "Consentimiento de prueba", "Texto."));
        await _consentimientos.EliminarAsync(id);

        await FluentActions
            .Awaiting(() => _consentimientos.PrepararImpresionAsync(id, _paciente, null))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // =========================================================

    private void EntrarComo(string rol, params string[] permisos)
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "test", "Usuario Test", rol, permisos,
            DateTime.UtcNow, 1);
    }

    private async Task<string?> EscalarAsync(string sql)
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        var valor = await cmd.ExecuteScalarAsync();
        return valor is null or DBNull ? null : Convert.ToString(valor);
    }

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertarAsync(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql + " SELECT LAST_INSERT_ID();";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
