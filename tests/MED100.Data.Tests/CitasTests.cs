using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Agenda contra MySQL real.
///
/// Lo que de verdad se prueba acá es la <b>ida y vuelta de la zona horaria</b>:
/// la hora que escribe la recepcionista es local de RD, la columna guarda UTC,
/// y lo que vuelve a la pantalla tiene que ser la misma hora que se escribió.
/// Un error de cuatro horas en esta cuenta no rompe nada visible hasta que un
/// paciente llega y su cita "no existe" — o peor, aparece de madrugada.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class CitasTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_citas_test;";

    private ConexionFactory _factory = null!;
    private CitaService _citas = null!;
    private CitaRepository _repo = null!;
    private MedicoRepository _medicosRepo = null!;

    private long _pacienteId;
    private long _medicoId;
    private long _procedimientoId;
    private long _usuarioId;

    /// <summary>Martes de la semana que viene: siempre futuro, nunca "ya pasó".</summary>
    private DateOnly _martes;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_citas_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _repo = new CitaRepository(_factory);
        _medicosRepo = new MedicoRepository(_factory);
        var procedimientosRepo = new ProcedimientoRepository(_factory);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        _citas = new CitaService(_repo, _medicosRepo, procedimientosRepo, auditoria);

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
            _medicoId = await InsertarAsync(conexion, """
                INSERT INTO medico (nombre, especialidad, porcentaje_honorario)
                VALUES ('Dr. Ramírez', 'Medicina general', 40.00);
                """);
            _procedimientoId = await InsertarAsync(conexion, """
                INSERT INTO procedimiento (nombre, precio, duracion_minutos, exento_itbis)
                VALUES ('Consulta', 1500.00, 45, 1);
                """);
        }

        SesionActual.Iniciar(_usuarioId, "test", "Usuario Test", "Admin",
            ["citas", "medicos", "procedimientos"], DateTime.UtcNow, 1);

        // Martes 8:00–12:00 y 14:00–18:00 (dia_semana 3 = martes en DAYOFWEEK)
        await _medicosRepo.InsertarHorarioAsync(_medicoId, new HorarioDatos(3, new(8, 0), new(12, 0)));
        await _medicosRepo.InsertarHorarioAsync(_medicoId, new HorarioDatos(3, new(14, 0), new(18, 0)));

        _martes = ProximoMartes();
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_citas_test;");
    }

    private static DateOnly ProximoMartes()
    {
        var dia = FechaNegocio.Hoy.AddDays(1);
        while (dia.DayOfWeek != DayOfWeek.Tuesday)
            dia = dia.AddDays(1);
        return dia;
    }

    private DateTime AlasDelMartes(int hora, int minuto = 0) =>
        _martes.ToDateTime(new TimeOnly(hora, minuto));

    private CitaDatos Cita(int hora, int minuto = 0, int duracion = 30, long? procedimiento = null) =>
        new(_pacienteId, _medicoId, procedimiento, AlasDelMartes(hora, minuto), duracion);

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserta y devuelve el id. Se usa LAST_INSERT_ID() en la MISMA conexión y
    /// en la misma sentencia: es por conexión, y desde otra devolvería 0.
    /// </summary>
    private static async Task<long> InsertarAsync(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql + "\nSELECT LAST_INSERT_ID();";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // =========================================================
    // Zona horaria: lo importante
    // =========================================================

    [Fact]
    public async Task Crear_LaHoraQueVuelveEsLaQueSeEscribio()
    {
        var id = await _citas.CrearAsync(Cita(9, 30));

        var leida = await _citas.ObtenerPorIdAsync(id);

        FechaNegocio.AUtcLocal(leida!.FechaHoraUtc).Should().Be(AlasDelMartes(9, 30));
    }

    [Fact]
    public async Task Crear_LaColumnaGuardaUtc_NoLaHoraLocal()
    {
        // RD es UTC-4 sin horario de verano: las 9:30 de la mañana acá son las
        // 13:30 UTC. Si la columna guardara la hora local, este test fallaría.
        var id = await _citas.CrearAsync(Cita(9, 30));

        var leida = await _citas.ObtenerPorIdAsync(id);

        leida!.FechaHoraUtc.Hour.Should().Be(13);
        leida.FechaHoraUtc.Minute.Should().Be(30);
    }

    [Fact]
    public async Task ObtenerPorDia_UsaElDiaDeNegocio_NoElDiaUtc()
    {
        // Una cita de las 8:00 AM local es de las 12:00 UTC del MISMO día, pero
        // el rango tiene que armarse en local: si se armara en UTC, las citas
        // de la tarde (después de las 8 PM local) caerían en el día siguiente.
        await _citas.CrearAsync(Cita(8));
        await _citas.CrearAsync(Cita(17, 30));

        var delDia = await _citas.ObtenerPorDiaAsync(_martes);

        delDia.Should().HaveCount(2);
        delDia.Select(c => FechaNegocio.AUtcLocal(c.FechaHoraUtc).Hour)
              .Should().BeEquivalentTo([8, 17]);
    }

    // =========================================================
    // Validación contra la base
    // =========================================================

    [Fact]
    public async Task Crear_FueraDelHorarioDelMedico_SeNiega()
    {
        var accion = async () => await _citas.CrearAsync(Cita(13));   // almuerzo

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*no atiende*");
    }

    [Fact]
    public async Task Crear_SobreOtraCita_SeNiega()
    {
        await _citas.CrearAsync(Cita(9, 0, 30));

        var accion = async () => await _citas.CrearAsync(Cita(9, 15, 30));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*ya tiene una cita*");
    }

    [Fact]
    public async Task Crear_PegadaALaAnterior_Pasa()
    {
        await _citas.CrearAsync(Cita(9, 0, 30));

        var accion = async () => await _citas.CrearAsync(Cita(9, 30, 30));

        await accion.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Crear_ConMedicoInactivo_SeNiega()
    {
        await _medicosRepo.ActualizarAsync(_medicoId,
            new MedicoDatos("Dr. Ramírez", null, null, "Medicina general", null, null, 40m, Activo: false));

        var accion = async () => await _citas.CrearAsync(Cita(9));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*inactivo*");
    }

    [Fact]
    public async Task Crear_ConMedicoInexistente_SeNiega()
    {
        var accion = async () => await _citas.CrearAsync(
            new CitaDatos(_pacienteId, 9999, null, AlasDelMartes(9), 30));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*no existe*");
    }

    [Fact]
    public async Task Crear_SinPermiso_SeNiega()
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "x", "Sin permiso", "Cajero", ["clientes"], DateTime.UtcNow, 1);

        var accion = async () => await _citas.CrearAsync(Cita(9));

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tienes permiso*");
    }

    // =========================================================
    // Datos por JOIN
    // =========================================================

    [Fact]
    public async Task Leer_TraeLosNombresDelPacienteDelMedicoYDelProcedimiento()
    {
        var id = await _citas.CrearAsync(Cita(9, 0, 45, _procedimientoId));

        var leida = await _citas.ObtenerPorIdAsync(id);

        leida!.PacienteNombre.Should().Be("María Pérez");
        leida.PacienteEmail.Should().Be("maria@gmail.com");
        leida.MedicoNombre.Should().Be("Dr. Ramírez");
        leida.ProcedimientoNombre.Should().Be("Consulta");
    }

    [Fact]
    public async Task Leer_SinProcedimiento_ElNombreQuedaNull()
    {
        // Consulta general: la cita no tiene procedimiento y el LEFT JOIN
        // devuelve NULL. Con un INNER JOIN la cita desaparecería de la agenda.
        var id = await _citas.CrearAsync(Cita(9));

        var leida = await _citas.ObtenerPorIdAsync(id);

        leida!.ProcedimientoId.Should().BeNull();
        leida.ProcedimientoNombre.Should().BeNull();
    }

    [Fact]
    public async Task Leer_ElNombreEsElACTUAL_NoUnaCopia()
    {
        // A diferencia de la factura, la cita NO congela los datos: si al
        // paciente le corrigen el nombre, la agenda muestra el corregido.
        var id = await _citas.CrearAsync(Cita(9));

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion,
                $"UPDATE cliente SET nombre = 'María Pérez Núñez' WHERE id = {_pacienteId};");
        }

        (await _citas.ObtenerPorIdAsync(id))!.PacienteNombre.Should().Be("María Pérez Núñez");
    }

    // =========================================================
    // Huecos y duración sugerida
    // =========================================================

    [Fact]
    public async Task Huecos_SacanLoQueYaEstaOcupado()
    {
        var antes = await _citas.HuecosLibresAsync(_medicoId, _martes, 30);
        await _citas.CrearAsync(Cita(9, 0, 30));
        var despues = await _citas.HuecosLibresAsync(_medicoId, _martes, 30);

        // Una cita de media hora tapa tres arranques de la grilla de 15 min.
        despues.Should().HaveCount(antes.Count - 3);
    }

    [Fact]
    public async Task Huecos_AlEditar_IncluyenLaHoraPropia()
    {
        var id = await _citas.CrearAsync(Cita(9, 0, 30));

        var huecos = await _citas.HuecosLibresAsync(_medicoId, _martes, 30, exceptoCitaId: id);

        huecos.Select(h => h.InicioLocal).Should().Contain(AlasDelMartes(9, 0));
    }

    [Fact]
    public async Task DuracionSugerida_SaleDelTarifario()
    {
        (await _citas.DuracionSugeridaAsync(_procedimientoId)).Should().Be(45);
    }

    [Fact]
    public async Task DuracionSugerida_SinProcedimiento_EsLaDeUnaConsulta()
    {
        (await _citas.DuracionSugeridaAsync(null)).Should().Be(30);
    }

    // =========================================================
    // Estados
    // =========================================================

    [Fact]
    public async Task CambiarEstado_ProgramadaAConfirmada_Pasa()
    {
        var id = await _citas.CrearAsync(Cita(9));

        await _citas.CambiarEstadoAsync(id, EstadoCita.Confirmada);

        (await _citas.ObtenerPorIdAsync(id))!.Estado.Should().Be(EstadoCita.Confirmada);
    }

    [Fact]
    public async Task CambiarEstado_DeUnaAtendidaHaciaAtras_SeNiega()
    {
        var id = await _citas.CrearAsync(Cita(9));
        await _citas.CambiarEstadoAsync(id, EstadoCita.Atendida);

        var accion = async () => await _citas.CambiarEstadoAsync(id, EstadoCita.Programada);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no puede pasar*");
    }

    [Fact]
    public async Task Cancelar_LiberaElHueco()
    {
        // Es la razón de que EstadoCita.Cancelada no ocupe agenda: si el
        // paciente avisa que no viene, esa hora se le puede dar a otro.
        var id = await _citas.CrearAsync(Cita(9, 0, 30));
        await _citas.CambiarEstadoAsync(id, EstadoCita.Cancelada);

        var accion = async () => await _citas.CrearAsync(Cita(9, 0, 30));

        await accion.Should().NotThrowAsync();
    }

    // =========================================================
    // Recordatorios
    // =========================================================

    [Fact]
    public async Task Pendientes_TraeLaCitaConCorreoQueTodaviaNoRecibioAviso()
    {
        var id = await _citas.CrearAsync(Cita(9));

        var pendientes = await _repo.ObtenerPendientesDeRecordatorioAsync(
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        pendientes.Should().ContainSingle(c => c.Id == id);
    }

    [Fact]
    public async Task Pendientes_NoRepiteLaQueYaSeAviso()
    {
        var id = await _citas.CrearAsync(Cita(9));
        await _repo.MarcarRecordatorioEnviadoAsync(id);

        var pendientes = await _repo.ObtenerPendientesDeRecordatorioAsync(
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        pendientes.Should().NotContain(c => c.Id == id);
    }

    [Fact]
    public async Task Pendientes_DejaFueraALasCanceladas()
    {
        var id = await _citas.CrearAsync(Cita(9));
        await _citas.CambiarEstadoAsync(id, EstadoCita.Cancelada);

        var pendientes = await _repo.ObtenerPendientesDeRecordatorioAsync(
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        pendientes.Should().NotContain(c => c.Id == id);
    }

    [Fact]
    public async Task Pendientes_DejaFueraAlPacienteSinCorreo()
    {
        long sinCorreo;
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            sinCorreo = await InsertarAsync(conexion,
                "INSERT INTO cliente (nombre) VALUES ('Juan Sin Correo');");
        }
        await _citas.CrearAsync(new CitaDatos(sinCorreo, _medicoId, null, AlasDelMartes(10), 30));

        var pendientes = await _repo.ObtenerPendientesDeRecordatorioAsync(
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        pendientes.Should().NotContain(c => c.ClienteId == sinCorreo);
    }

    [Fact]
    public async Task Pendientes_DejaFueraALasQueCaenDespuesDeLaVentana()
    {
        await _citas.CrearAsync(Cita(9));

        // Ventana de una hora: la cita del martes que viene no entra.
        var pendientes = await _repo.ObtenerPendientesDeRecordatorioAsync(
            DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

        pendientes.Should().BeEmpty();
    }

    // =========================================================
    // Edición y borrado
    // =========================================================

    [Fact]
    public async Task Actualizar_MueveLaCitaDeHora()
    {
        var id = await _citas.CrearAsync(Cita(9));

        await _citas.ActualizarAsync(id, Cita(15, 30));

        var leida = await _citas.ObtenerPorIdAsync(id);
        FechaNegocio.AUtcLocal(leida!.FechaHoraUtc).Should().Be(AlasDelMartes(15, 30));
    }

    [Fact]
    public async Task Actualizar_ALaMismaHora_NoChocaConsigoMisma()
    {
        var id = await _citas.CrearAsync(Cita(9, 0, 30));

        // Solo cambia la nota: la hora sigue siendo la suya.
        var accion = async () => await _citas.ActualizarAsync(id,
            Cita(9, 0, 30) with { Notas = "Viene con su mamá" });

        await accion.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Eliminar_UnaCitaSinFactura_SeBorra()
    {
        var id = await _citas.CrearAsync(Cita(9));

        await _citas.EliminarAsync(id);

        (await _citas.ObtenerPorIdAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task Historial_TraeLasCitasDelPaciente()
    {
        await _citas.CrearAsync(Cita(9));
        await _citas.CrearAsync(Cita(15));

        var historial = await _citas.ObtenerDelPacienteAsync(_pacienteId);

        historial.Should().HaveCount(2);
        // De la más reciente a la más vieja
        historial[0].FechaHoraUtc.Should().BeAfter(historial[1].FechaHoraUtc);
    }

    [Fact]
    public async Task Auditoria_QuedaRegistroDeLaCreacionYDelCambioDeEstado()
    {
        var id = await _citas.CrearAsync(Cita(9));
        await _citas.CambiarEstadoAsync(id, EstadoCita.Confirmada);

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM auditoria WHERE entidad = 'cita' AND entidad_id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        Convert.ToInt64(await cmd.ExecuteScalarAsync()).Should().Be(2);
    }
}
