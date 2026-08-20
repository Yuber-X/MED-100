using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Sala de espera contra MySQL real.
///
/// Lo que de verdad importa acá es la <b>numeración</b>: reinicia cada día y no
/// puede repetirse aunque dos recepcionistas den turno al mismo tiempo. Dos
/// papelitos con el mismo "15" es una discusión en el mostrador que el sistema
/// no puede desempatar.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class TurnosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_turnos_test;";

    private ConexionFactory _factory = null!;
    private TurnoRepository _repo = null!;
    private TurnoService _servicio = null!;
    private ConfiguracionNegocioService _negocio = null!;

    private long _pacienteId;
    private long _medicoId;
    private long _usuarioId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_turnos_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _repo = new TurnoRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        _negocio = new ConfiguracionNegocioService(new ConfiguracionNegocioRepository(_factory), auditoria);
        await _negocio.CargarAsync();
        _servicio = new TurnoService(_repo, auditoria, _negocio);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            _pacienteId = await InsertarAsync(conexion,
                "INSERT INTO cliente (nombre) VALUES ('María Pérez');");
            _medicoId = await InsertarAsync(conexion,
                "INSERT INTO medico (nombre, porcentaje_honorario) VALUES ('Dr. Ramírez', 40.00);");
        }

        // "configuracion" hace falta para el test del prefijo: el prefijo del
        // turno vive en configuracion_negocio y solo el Admin la toca.
        SesionActual.Iniciar(_usuarioId, "test", "Usuario Test", "Admin",
            ["turnos", "configuracion"], DateTime.UtcNow, 1);
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_turnos_test;");
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
        cmd.CommandText = sql + "\nSELECT LAST_INSERT_ID();";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // =========================================================
    // Numeración
    // =========================================================

    [Fact]
    public async Task Dar_ElPrimerTurnoDelDiaEsElUno()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos());

        turno.Numero.Should().Be(1);
        turno.Fecha.Should().Be(FechaNegocio.Hoy);
        turno.Estado.Should().Be(EstadoTurno.Esperando);
    }

    [Fact]
    public async Task Dar_LosNumerosVanEnOrden()
    {
        var a = await _servicio.DarAsync(new TurnoDatos());
        var b = await _servicio.DarAsync(new TurnoDatos());
        var c = await _servicio.DarAsync(new TurnoDatos());

        new[] { a.Numero, b.Numero, c.Numero }.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task Dar_ElNumeroReiniciaCadaDia()
    {
        // Turno de ayer metido a mano: la numeración de hoy no debe continuarlo.
        await _repo.DarSiguienteAsync(FechaNegocio.Hoy.AddDays(-1), new TurnoDatos());
        await _repo.DarSiguienteAsync(FechaNegocio.Hoy.AddDays(-1), new TurnoDatos());

        var deHoy = await _servicio.DarAsync(new TurnoDatos());

        deHoy.Numero.Should().Be(1);
    }

    [Fact]
    public async Task Dar_EnParalelo_NingunNumeroSeRepite()
    {
        // Diez recepcionistas imaginarias apretando el botón a la vez. Sin la
        // reserva atómica, varias leerían el mismo MAX(numero) y saldrían
        // papelitos repetidos.
        const int cuantos = 10;

        var tareas = Enumerable.Range(0, cuantos)
            .Select(_ => _repo.DarSiguienteAsync(FechaNegocio.Hoy, new TurnoDatos()))
            .ToArray();
        var turnos = await Task.WhenAll(tareas);

        var numeros = turnos.Select(t => t.Numero).ToList();
        numeros.Should().OnlyHaveUniqueItems();
        numeros.Should().BeEquivalentTo(Enumerable.Range(1, cuantos));
    }

    [Fact]
    public async Task Etiqueta_SinPrefijo_EsSoloElNumero()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos());

        _servicio.Etiqueta(turno).Should().Be("1");
    }

    [Fact]
    public async Task Etiqueta_ConPrefijo_LoAntepone()
    {
        var cfg = _negocio.Actual;
        cfg.TurnoPrefijo = "A";
        await _negocio.GuardarAsync(cfg);

        var turno = await _servicio.DarAsync(new TurnoDatos());

        _servicio.Etiqueta(turno).Should().Be("A-1");
    }

    // =========================================================
    // El turno se da antes de saber quién es
    // =========================================================

    [Fact]
    public async Task Dar_SinPacienteNiMedico_Funciona()
    {
        // Es el caso normal: el número se entrega en la puerta.
        var turno = await _servicio.DarAsync(new TurnoDatos());

        turno.ClienteId.Should().BeNull();
        turno.PacienteNombre.Should().BeNull();
        turno.MedicoId.Should().BeNull();
    }

    [Fact]
    public async Task Dar_ConPacienteYMedico_LosTraePorJoin()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos(_pacienteId, _medicoId));

        turno.PacienteNombre.Should().Be("María Pérez");
        turno.MedicoNombre.Should().Be("Dr. Ramírez");
    }

    [Fact]
    public async Task AsignarPaciente_LlenaUnTurnoQueSeDioEnBlanco()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos());

        await _servicio.AsignarPacienteAsync(turno.Id, _pacienteId);

        var leido = await _servicio.ObtenerPorIdAsync(turno.Id);
        leido!.ClienteId.Should().Be(_pacienteId);
        leido.PacienteNombre.Should().Be("María Pérez");
    }

    // =========================================================
    // Llamar y cerrar
    // =========================================================

    [Fact]
    public async Task LlamarSiguiente_TomaElDeMenorNumeroQueEspera()
    {
        await _servicio.DarAsync(new TurnoDatos());
        await _servicio.DarAsync(new TurnoDatos());

        var llamado = await _servicio.LlamarSiguienteAsync();

        llamado!.Numero.Should().Be(1);
        llamado.Estado.Should().Be(EstadoTurno.Llamado);
    }

    [Fact]
    public async Task LlamarSiguiente_SaltaAlQueYaSeLlamo()
    {
        await _servicio.DarAsync(new TurnoDatos());
        await _servicio.DarAsync(new TurnoDatos());
        await _servicio.LlamarSiguienteAsync();

        var segundo = await _servicio.LlamarSiguienteAsync();

        segundo!.Numero.Should().Be(2);
    }

    [Fact]
    public async Task LlamarSiguiente_SalaVacia_DevuelveNull()
    {
        (await _servicio.LlamarSiguienteAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Llamar_SellaLaHora()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos());

        var llamado = await _servicio.LlamarAsync(turno.Id);

        llamado.LlamadoAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Cerrar_NoPisaLaHoraDelLlamado()
    {
        // llamado_at es de donde sale cuánto esperó la gente. Marcarlo atendido
        // no puede reescribirlo, o el dato de espera se pierde.
        var turno = await _servicio.DarAsync(new TurnoDatos());
        var llamado = await _servicio.LlamarAsync(turno.Id);
        var hora = llamado.LlamadoAtUtc;

        await _servicio.CerrarAsync(turno.Id, EstadoTurno.Atendido);

        (await _servicio.ObtenerPorIdAsync(turno.Id))!.LlamadoAtUtc.Should().Be(hora);
    }

    [Fact]
    public async Task Llamar_UnTurnoYaCerrado_SeNiega()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos());
        await _servicio.CerrarAsync(turno.Id, EstadoTurno.Atendido);

        var accion = async () => await _servicio.LlamarAsync(turno.Id);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dale un turno nuevo*");
    }

    [Fact]
    public async Task Cerrar_ConUnEstadoQueNoEsFinal_SeNiega()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos());

        var accion = async () => await _servicio.CerrarAsync(turno.Id, EstadoTurno.Esperando);

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*atendido o ausente*");
    }

    [Fact]
    public async Task Ausente_EsDistintoDeAtendido()
    {
        // Separarlos es lo que deja ver, al final del día, cuánta gente se
        // cansó de esperar y se fue.
        var a = await _servicio.DarAsync(new TurnoDatos());
        var b = await _servicio.DarAsync(new TurnoDatos());
        await _servicio.CerrarAsync(a.Id, EstadoTurno.Atendido);
        await _servicio.CerrarAsync(b.Id, EstadoTurno.Ausente);

        var resumen = await _servicio.ResumenAsync();

        resumen.Atendidos.Should().Be(1);
        resumen.Ausentes.Should().Be(1);
    }

    // =========================================================
    // Tablero
    // =========================================================

    [Fact]
    public async Task Resumen_SalaVacia_DaCeros()
    {
        var resumen = await _servicio.ResumenAsync();

        resumen.Total.Should().Be(0);
        resumen.Esperando.Should().Be(0);
    }

    [Fact]
    public async Task Resumen_CuentaCadaEstado()
    {
        var a = await _servicio.DarAsync(new TurnoDatos());
        await _servicio.DarAsync(new TurnoDatos());
        var c = await _servicio.DarAsync(new TurnoDatos());
        await _servicio.LlamarAsync(a.Id);
        await _servicio.CerrarAsync(c.Id, EstadoTurno.Atendido);

        var resumen = await _servicio.ResumenAsync();

        resumen.Esperando.Should().Be(1);
        resumen.Llamados.Should().Be(1);
        resumen.Atendidos.Should().Be(1);
        resumen.Total.Should().Be(3);
    }

    [Fact]
    public async Task ObtenerDelDia_SoloTraeLosDeHoy()
    {
        await _repo.DarSiguienteAsync(FechaNegocio.Hoy.AddDays(-1), new TurnoDatos());
        await _servicio.DarAsync(new TurnoDatos());

        var deHoy = await _servicio.ObtenerDelDiaAsync();

        deHoy.Should().HaveCount(1);
        deHoy[0].Fecha.Should().Be(FechaNegocio.Hoy);
    }

    // =========================================================
    // Permisos y auditoría
    // =========================================================

    [Fact]
    public async Task Dar_SinPermiso_SeNiega()
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "x", "Sin permiso", "Cajero", ["clientes"], DateTime.UtcNow, 1);

        var accion = async () => await _servicio.DarAsync(new TurnoDatos());

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tienes permiso*");
    }

    [Fact]
    public async Task Auditoria_QuedaRegistroDeDarYDeLlamar()
    {
        var turno = await _servicio.DarAsync(new TurnoDatos());
        await _servicio.LlamarAsync(turno.Id);

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM auditoria WHERE entidad = 'turno' AND entidad_id = @id;";
        cmd.Parameters.AddWithValue("@id", turno.Id);

        Convert.ToInt64(await cmd.ExecuteScalarAsync()).Should().Be(2);
    }
}
