using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// La ficha del paciente ("Ver detalles", pedido de Yuber 2026-08-14).
///
/// Lo delicado acá es que las cuatro listas salen de UNA sola consulta con
/// cinco resultados encadenados: si el orden de lectura se desfasa, la
/// pantalla mostraría las facturas de otro o se caería al mapear. Y los
/// totales tienen que ignorar las facturas anuladas — decirle a un paciente
/// que debe algo que se le anuló es una discusión en el mostrador.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class HistorialPacienteTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_historial_test;";

    private ConexionFactory _factory = null!;
    private ClienteService _pacientes = null!;
    private VentaService _ventas = null!;
    private CitaService _citas = null!;
    private TurnoService _turnos = null!;
    private FacturaService _facturas = null!;

    private long _usuarioId;
    private long _pacienteId;
    private long _otroPacienteId;
    private long _medicoId;
    private long _procedimientoId;
    private long _productoId;
    private DateOnly _martes;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_historial_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        var medicosRepo = new MedicoRepository(_factory);
        var negocio = new ConfiguracionNegocioService(
            new ConfiguracionNegocioRepository(_factory), auditoria);
        await negocio.CargarAsync();

        var facturaRepo = new FacturaRepository(_factory);
        _pacientes = new ClienteService(new ClienteRepository(_factory),
            new HistorialRepository(_factory), auditoria);
        _ventas = new VentaService(facturaRepo, new ClienteRepository(_factory),
            medicosRepo, new ArsRepository(_factory), negocio, auditoria,
            new NcfService(new NcfRepository(_factory), auditoria));
        _facturas = new FacturaService(facturaRepo, auditoria);
        _citas = new CitaService(new CitaRepository(_factory), medicosRepo,
            new ProcedimientoRepository(_factory), auditoria);
        _turnos = new TurnoService(new TurnoRepository(_factory), auditoria, negocio);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            _pacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre, telefono, email)
                VALUES ('María Pérez', '809-555-1234', 'maria@gmail.com');
                """);
            _otroPacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre) VALUES ('Juan Otro');
                """);
            _medicoId = await InsertarAsync(conexion, """
                INSERT INTO medico (nombre, especialidad, porcentaje_honorario, codigo_turno)
                VALUES ('Dr. Ramírez', 'Medicina general', 40.00, 'DZ');
                """);
            _procedimientoId = await InsertarAsync(conexion, """
                INSERT INTO procedimiento (nombre, precio, duracion_minutos, exento_itbis)
                VALUES ('Consulta general', 1500.00, 30, 1);
                """);
            _productoId = await InsertarAsync(conexion, """
                INSERT INTO producto (nombre, precio, cantidad)
                VALUES ('Gasa estéril', 100.00, 500);
                """);
        }

        SesionActual.Iniciar(_usuarioId, "test", "Usuario Test", "Admin",
            ["clientes", "clientes_editar", "vender", "citas", "turnos", "medicos",
             "procedimientos", "facturas_anular"],
            DateTime.UtcNow, 1);

        await new MedicoRepository(_factory)
            .InsertarHorarioAsync(_medicoId, new HorarioDatos(3, new(8, 0), new(18, 0)));
        _martes = ProximoMartes();
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_historial_test;");
    }

    // =========================================================

    [Fact]
    public async Task Historial_PacienteSinNada_DevuelveTodoVacio()
    {
        var h = await _pacientes.ObtenerHistorialAsync(_otroPacienteId);

        h.Paciente.Nombre.Should().Be("Juan Otro");
        h.Citas.Should().BeEmpty();
        h.Turnos.Should().BeEmpty();
        h.Facturas.Should().BeEmpty();
        h.Procedimientos.Should().BeEmpty();
        h.UltimaVisitaUtc.Should().BeNull();
        h.TotalFacturado.Should().Be(0m);
    }

    [Fact]
    public async Task Historial_TraeLasCuatroListasDelPacienteCorrecto()
    {
        await _citas.CrearAsync(new CitaDatos(_pacienteId, _medicoId, _procedimientoId,
            _martes.ToDateTime(new TimeOnly(9, 0)), 30));
        await _turnos.DarAsync(new TurnoDatos(_pacienteId, _medicoId));
        await CobrarConsultaAsync(_pacienteId);

        // Ruido de otro paciente: si la consulta se cruza, aparecería acá
        await _turnos.DarAsync(new TurnoDatos(_otroPacienteId, _medicoId));
        await CobrarConsultaAsync(_otroPacienteId);

        var h = await _pacientes.ObtenerHistorialAsync(_pacienteId);

        h.Citas.Should().ContainSingle();
        h.Turnos.Should().ContainSingle();
        h.Facturas.Should().ContainSingle();
        h.Procedimientos.Should().ContainSingle();
    }

    [Fact]
    public async Task Historial_LosProcedimientosSalenDeLoFacturado_NoDeLosInsumos()
    {
        // La lista de "procedimientos" es lo más cerca del "qué le hicieron"
        // que MED-100 puede mostrar sin ser expediente clínico. La gasa que se
        // le vendió NO es un procedimiento.
        await _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [VentaLinea.DeProcedimiento(_procedimientoId, "Consulta general", 1, 1500m),
             VentaLinea.DeInsumo(_productoId, "Gasa estéril", 2, 100m)],
            _pacienteId, MetodoPagoFactura.Efectivo, 2000m, _medicoId));

        var h = await _pacientes.ObtenerHistorialAsync(_pacienteId);

        h.Procedimientos.Should().ContainSingle()
            .Which.Descripcion.Should().Be("Consulta general");
        h.Procedimientos[0].MedicoNombre.Should().Be("Dr. Ramírez");
    }

    [Fact]
    public async Task Historial_LosTotalesIgnoranLasFacturasAnuladas()
    {
        var vigente = await CobrarConsultaAsync(_pacienteId);
        var anulada = await CobrarConsultaAsync(_pacienteId);

        await _facturas.AnularAsync(anulada.FacturaId, "Cobro duplicado");

        var h = await _pacientes.ObtenerHistorialAsync(_pacienteId);

        h.Facturas.Should().HaveCount(2, "la anulada sigue en el historial, no se borra");
        h.Facturas.Should().Contain(f => f.Anulada);
        h.TotalFacturado.Should().Be(1500m, "solo cuenta la que sigue en pie");
        h.TotalPagadoPorElPaciente.Should().Be(1500m);
        _ = vigente;
    }

    [Fact]
    public async Task Historial_ConArs_SeparaLoQueCubrioElSeguro()
    {
        long arsId;
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            arsId = await InsertarAsync(conexion, "INSERT INTO ars (nombre) VALUES ('ARS Prueba');");
        }

        await _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [VentaLinea.DeProcedimiento(_procedimientoId, "Consulta general", 1, 1500m)],
            _pacienteId, MetodoPagoFactura.Efectivo, 500m, _medicoId,
            ArsId: arsId, ArsCubierto: 1200m));

        var h = await _pacientes.ObtenerHistorialAsync(_pacienteId);

        h.TotalFacturado.Should().Be(1500m);
        h.TotalCubiertoPorArs.Should().Be(1200m);
        h.TotalPagadoPorElPaciente.Should().Be(300m, "el paciente solo puso su parte");
        h.Facturas[0].ArsNombre.Should().Be("ARS Prueba");
    }

    [Fact]
    public async Task Historial_CuentaLasCitasAtendidasYLasPerdidas()
    {
        var atendida = await _citas.CrearAsync(new CitaDatos(_pacienteId, _medicoId, null,
            _martes.ToDateTime(new TimeOnly(9, 0)), 30));
        var perdida = await _citas.CrearAsync(new CitaDatos(_pacienteId, _medicoId, null,
            _martes.ToDateTime(new TimeOnly(10, 0)), 30));
        await _citas.CrearAsync(new CitaDatos(_pacienteId, _medicoId, null,
            _martes.ToDateTime(new TimeOnly(11, 0)), 30));

        await _citas.CambiarEstadoAsync(atendida, EstadoCita.Atendida);
        await _citas.CambiarEstadoAsync(perdida, EstadoCita.NoAsistio);

        var h = await _pacientes.ObtenerHistorialAsync(_pacienteId);

        h.Citas.Should().HaveCount(3);
        h.CitasAtendidas.Should().Be(1);
        h.CitasPerdidas.Should().Be(1);
    }

    [Fact]
    public async Task Historial_ElTurnoTraeElCodigoDelMedico()
    {
        // Es lo que hace que en la ficha se lea "DZ-1" y no un "1" pelado.
        await _turnos.DarAsync(new TurnoDatos(_pacienteId, _medicoId));

        var h = await _pacientes.ObtenerHistorialAsync(_pacienteId);

        h.Turnos.Should().ContainSingle()
            .Which.MedicoCodigoTurno.Should().Be("DZ");
    }

    [Fact]
    public async Task Historial_DePacienteQueNoExiste_SeNiegaConMensajeClaro()
    {
        var accion = () => _pacientes.ObtenerHistorialAsync(999999);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no existe*");
    }

    [Fact]
    public async Task Historial_SinPermisoDeClientes_SeNiega()
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "x", "Sin permiso", "Servicio",
            ["turnos"], DateTime.UtcNow, 2);

        var accion = () => _pacientes.ObtenerHistorialAsync(_pacienteId);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*permiso*");
    }

    [Fact]
    public async Task Historial_QuedaEnAuditoria()
    {
        // Datos personales: la Ley 172-13 obliga a poder decir quién los miró.
        await _pacientes.ObtenerHistorialAsync(_pacienteId);

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM auditoria
            WHERE entidad = 'cliente' AND entidad_id = @id AND accion = 'consultar';
            """;
        cmd.Parameters.AddWithValue("@id", _pacienteId);

        Convert.ToInt64(await cmd.ExecuteScalarAsync()).Should().Be(1);
    }

    // =========================================================

    private Task<VentaResultado> CobrarConsultaAsync(long pacienteId) =>
        _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [VentaLinea.DeProcedimiento(_procedimientoId, "Consulta general", 1, 1500m)],
            pacienteId, MetodoPagoFactura.Efectivo, 1500m, _medicoId));

    private static DateOnly ProximoMartes()
    {
        var dia = FechaNegocio.Hoy.AddDays(1);
        while (dia.DayOfWeek != DayOfWeek.Tuesday)
            dia = dia.AddDays(1);
        return dia;
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
