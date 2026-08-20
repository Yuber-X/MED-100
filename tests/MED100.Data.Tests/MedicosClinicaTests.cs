using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Lo que se le agregó al médico el 2026-08-14: el <b>código de turno</b> con
/// que se identifican sus números en la sala, y los <b>días fijos</b> que se
/// marcan de un tirón desde el formulario.
///
/// Las dos cosas tocan datos que la recepción ve todo el día, y la de los días
/// además BORRA tramos: un error ahí deja a un médico sin horario y la agenda
/// deja de ofrecer huecos sin decir por qué.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class MedicosClinicaTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_medicos_test;";

    private ConexionFactory _factory = null!;
    private MedicoRepository _repo = null!;
    private MedicoService _servicio = null!;
    private long _usuarioId;

    // Convención DAYOFWEEK() de MySQL: 1 = domingo … 7 = sábado
    private const int Lunes = 2;
    private const int Martes = 3;
    private const int Miercoles = 4;
    private const int Viernes = 6;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_medicos_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _repo = new MedicoRepository(_factory);
        _servicio = new MedicoService(_repo, new AuditoriaService(new AuditoriaRepository(_factory)));

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
        }

        SesionActual.Iniciar(_usuarioId, "test", "Usuario Test", "Admin",
            ["medicos", "citas"], DateTime.UtcNow, 1);
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_medicos_test;");
    }

    private static MedicoDatos Datos(string nombre, string? codigo = null) =>
        new(nombre, null, null, "Medicina general", null, null, 40m, true, codigo);

    // =========================================================
    // Código de turno
    // =========================================================

    [Fact]
    public async Task Crear_SinCodigo_LoCalculaDelNombre()
    {
        var id = await _servicio.CrearAsync(Datos("Yuber Santana Lizardo"));

        var medico = await _servicio.ObtenerPorIdAsync(id);
        medico!.CodigoTurno.Should().Be("YO");
    }

    [Fact]
    public async Task Crear_ConCodigoEscrito_LoRespeta()
    {
        var id = await _servicio.CrearAsync(Datos("Yuber Santana Lizardo", "ysl"));

        var medico = await _servicio.ObtenerPorIdAsync(id);
        medico!.CodigoTurno.Should().Be("YSL", "se normaliza a mayúsculas pero no se recalcula");
    }

    [Fact]
    public async Task Crear_DosMedicosConLasMismasIniciales_NoChocan()
    {
        // "Yuber Santana Lizardo" y "Yolanda Peña Prado" dan los dos "YO".
        // Sin la búsqueda de variante, el segundo alta reventaría contra el
        // índice único con un error de MySQL en la cara del usuario.
        var primero = await _servicio.CrearAsync(Datos("Yuber Santana Lizardo"));
        var segundo = await _servicio.CrearAsync(Datos("Yolanda Peña Prado"));

        var a = await _servicio.ObtenerPorIdAsync(primero);
        var b = await _servicio.ObtenerPorIdAsync(segundo);

        a!.CodigoTurno.Should().Be("YO");
        b!.CodigoTurno.Should().Be("YO2");
    }

    [Fact]
    public async Task Crear_ConCodigoQueYaEsDeOtro_SeNiegaConMensajeClaro()
    {
        await _servicio.CrearAsync(Datos("Ingrid Lizardo", "IO"));

        var accion = () => _servicio.CrearAsync(Datos("Otro Médico", "IO"));

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ya lo tiene otro médico*");
    }

    [Fact]
    public async Task Actualizar_SinTocarElCodigo_NoSeLoQuitaASiMismo()
    {
        // El chequeo de duplicado tiene que excluir al propio médico: si no,
        // guardar dos veces seguidas diría que su código ya está tomado.
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));

        await _servicio.ActualizarAsync(id, Datos("Ingrid Lizardo", "IO"));

        var medico = await _servicio.ObtenerPorIdAsync(id);
        medico!.CodigoTurno.Should().Be("IO");
    }

    // =========================================================
    // Días fijos
    // =========================================================

    [Fact]
    public async Task DiasFijos_MarcaTresDias_CreaUnTramoEnCadaUno()
    {
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));

        await _servicio.SincronizarDiasFijosAsync(id, [Lunes, Martes, Viernes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        var horarios = await _servicio.ObtenerHorariosAsync(id);
        horarios.Select(h => h.DiaSemana).Should().BeEquivalentTo([Lunes, Martes, Viernes]);
        horarios.Should().OnlyContain(h => h.HoraInicio == new TimeOnly(8, 0) &&
                                           h.HoraFin == new TimeOnly(17, 0));
    }

    [Fact]
    public async Task DiasFijos_DestildarUnDia_LeQuitaTodosSusTramos()
    {
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));
        await _servicio.SincronizarDiasFijosAsync(id, [Lunes, Martes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        await _servicio.SincronizarDiasFijosAsync(id, [Lunes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        var horarios = await _servicio.ObtenerHorariosAsync(id);
        horarios.Should().ContainSingle().Which.DiaSemana.Should().Be(Lunes);
    }

    [Fact]
    public async Task DiasFijos_UnDiaQueYaTeniaMananaYTarde_NoSeToca()
    {
        // Es la regla que salva el corte del almuerzo: reescribir el día
        // fusionaría los dos tramos en uno de 8 a 17 y la agenda empezaría a
        // ofrecer turnos a la hora de comer.
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));
        await _repo.InsertarHorarioAsync(id, new HorarioDatos(Lunes, new(8, 0), new(12, 0)));
        await _repo.InsertarHorarioAsync(id, new HorarioDatos(Lunes, new(14, 0), new(18, 0)));

        await _servicio.SincronizarDiasFijosAsync(id, [Lunes, Martes],
            new TimeOnly(9, 0), new TimeOnly(17, 0));

        var delLunes = (await _servicio.ObtenerHorariosAsync(id))
            .Where(h => h.DiaSemana == Lunes).ToList();

        delLunes.Should().HaveCount(2, "los dos tramos del lunes siguen intactos");
        delLunes.Should().Contain(h => h.HoraInicio == new TimeOnly(8, 0) && h.HoraFin == new TimeOnly(12, 0));
        delLunes.Should().Contain(h => h.HoraInicio == new TimeOnly(14, 0) && h.HoraFin == new TimeOnly(18, 0));
    }

    [Fact]
    public async Task DiasFijos_SinNingunDia_LoDejaSinHorario()
    {
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));
        await _servicio.SincronizarDiasFijosAsync(id, [Lunes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        await _servicio.SincronizarDiasFijosAsync(id, [],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        (await _servicio.ObtenerHorariosAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task DiasFijos_GuardarDosVecesLoMismo_NoDuplicaTramos()
    {
        // Reabrir el formulario y volver a guardar sin cambiar nada es lo que
        // más se hace: no puede ir sumando tramos idénticos.
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));

        await _servicio.SincronizarDiasFijosAsync(id, [Lunes, Miercoles],
            new TimeOnly(8, 0), new TimeOnly(17, 0));
        await _servicio.SincronizarDiasFijosAsync(id, [Lunes, Miercoles],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        (await _servicio.ObtenerHorariosAsync(id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task DiasFijos_HoraDeSalidaAntesDeLaDeEntrada_SeNiega()
    {
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));

        var accion = () => _servicio.SincronizarDiasFijosAsync(id, [Lunes],
            new TimeOnly(17, 0), new TimeOnly(8, 0));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*posterior*");
    }

    [Fact]
    public async Task DiasFijos_DiaFueraDeRango_SeNiega()
    {
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));

        var accion = () => _servicio.SincronizarDiasFijosAsync(id, [9],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        await accion.Should().ThrowAsync<ArgumentException>();
    }

    // =========================================================
    // Quién atiende cada día (alimenta el combo de Nueva cita)
    // =========================================================

    [Fact]
    public async Task QueAtienden_SoloDevuelveALosQueTrabajanEseDia()
    {
        var ingrid = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));
        var pedro = await _servicio.CrearAsync(Datos("Pedro Sosa"));

        await _servicio.SincronizarDiasFijosAsync(ingrid, [Lunes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));
        await _servicio.SincronizarDiasFijosAsync(pedro, [Martes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        var delLunes = await _servicio.ObtenerQueAtiendenAsync(ProximoDia(DayOfWeek.Monday));

        delLunes.Should().ContainSingle().Which.Id.Should().Be(ingrid);
    }

    [Fact]
    public async Task QueAtienden_UnMedicoInactivo_NoAparece()
    {
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));
        await _servicio.SincronizarDiasFijosAsync(id, [Lunes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        await _servicio.ActualizarAsync(id, Datos("Ingrid Lizardo") with { Activo = false });

        var delLunes = await _servicio.ObtenerQueAtiendenAsync(ProximoDia(DayOfWeek.Monday));
        delLunes.Should().BeEmpty();
    }

    [Fact]
    public async Task Disponibilidad_TraeLosDiasFijos_ParaLaColumnaDeLaTabla()
    {
        var id = await _servicio.CrearAsync(Datos("Ingrid Lizardo"));
        await _servicio.SincronizarDiasFijosAsync(id, [Lunes, Martes, Viernes],
            new TimeOnly(8, 0), new TimeOnly(17, 0));

        var disponibilidad = await _servicio.ObtenerDisponibilidadAsync();

        var ingrid = disponibilidad.Single(d => d.Medico.Id == id);
        // Ordenados de lunes a domingo, no de domingo a sábado como los numera MySQL
        ingrid.DiasQueAtiende.Should().Equal([Lunes, Martes, Viernes]);
        DisponibilidadMedicos.ResumirDias(ingrid.DiasQueAtiende).Should().Be("Lun, Mar, Vie");
    }

    // =========================================================
    // Ayudas
    // =========================================================

    private static DateOnly ProximoDia(DayOfWeek objetivo)
    {
        var dia = FechaNegocio.Hoy.AddDays(1);
        while (dia.DayOfWeek != objetivo)
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
