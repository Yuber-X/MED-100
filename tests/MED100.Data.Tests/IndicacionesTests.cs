using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Medicamentos indicados (013) y los dos pedidos de la clínica del 2026-09-21:
/// el listado de los que más se usan, y poder quitar o corregir UNO solo de una
/// indicación ya guardada.
///
/// Lo delicado no es el SQL: es que corregir no se convierta en borrar sin
/// rastro. Por eso acá se verifica que la auditoría guarde el ANTES y el
/// DESPUÉS con los nombres, y que lo de días anteriores no lo toque el
/// mostrador.
///
/// ⚠ Contenido clínico: los datos son inventados a propósito (CLAUDE.md §1.1).
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class IndicacionesTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_indicaciones_test;";

    private ConexionFactory _factory = null!;
    private IndicacionRepository _repo = null!;
    private IndicacionService _indicaciones = null!;

    private long _usuarioId;
    private long _pacienteId;
    private long _otroPacienteId;
    private long _medicoId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_indicaciones_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();
        await new VerificadorBaseDatos(CadenaTest).ActualizarEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        _repo = new IndicacionRepository(_factory);
        _indicaciones = new IndicacionService(_repo, auditoria);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            _pacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre) VALUES ('Paciente Uno');
                """);
            _otroPacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre) VALUES ('Paciente Dos');
                """);
            _medicoId = await InsertarAsync(conexion, """
                INSERT INTO medico (nombre, especialidad, porcentaje_honorario, codigo_turno)
                VALUES ('Dra. Prueba', 'Odontología', 40.00, 'DP');
                """);
        }

        EntrarComo("Admin");
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_indicaciones_test;");
    }

    // ================= Los más usados =================

    [Fact]
    public async Task MasUsados_OrdenaPorFrecuencia_YTraeLaDosisDeLaUltimaVez()
    {
        await IndicarAsync(_pacienteId, ("Acetaminofén", "500 mg"));
        await IndicarAsync(_otroPacienteId, ("Acetaminofén", "650 mg"));
        await IndicarAsync(_pacienteId, ("Amoxicilina", "500 mg"));

        var masUsados = await _indicaciones.ObtenerMasUsadosAsync();

        masUsados.Should().HaveCount(2);
        masUsados[0].Medicamento.Should().Be("Acetaminofén", "es el que más se indicó");
        masUsados[0].Veces.Should().Be(2);
        masUsados[0].Dosis.Should().Be("650 mg",
            "se propone la dosis de la última vez, no la de la primera");
        masUsados[1].Medicamento.Should().Be("Amoxicilina");
        masUsados[1].Veces.Should().Be(1);
    }

    [Fact]
    public async Task MasUsados_NoCuentaLoQueSeDioDeBaja()
    {
        await IndicarAsync(_pacienteId, ("Ibuprofeno", "400 mg"));
        var repetida = await IndicarAsync(_otroPacienteId, ("Ibuprofeno", "600 mg"));

        await _indicaciones.EliminarAsync(repetida, "Cargada al paciente equivocado");

        var masUsados = await _indicaciones.ObtenerMasUsadosAsync();

        masUsados.Should().ContainSingle();
        masUsados[0].Veces.Should().Be(1, "la dada de baja no cuenta");
        masUsados[0].Dosis.Should().Be("400 mg",
            "la última que vale es la de la indicación que sigue en pie");
    }

    [Fact]
    public async Task MasUsados_RespetaElTope()
    {
        await IndicarAsync(_pacienteId,
            ("Uno", "1"), ("Dos", "2"), ("Tres", "3"), ("Cuatro", "4"));

        var masUsados = await _indicaciones.ObtenerMasUsadosAsync(tope: 2);

        masUsados.Should().HaveCount(2);
    }

    [Fact]
    public async Task MasUsados_SinPermiso_SeNiega()
    {
        // Es contenido clínico: hasta la lista de nombres sale del historial de
        // pacientes reales, así que exige el mismo permiso que todo lo demás.
        EntrarComo("Cajero", "vender");

        await FluentActions.Awaiting(() => _indicaciones.ObtenerMasUsadosAsync())
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ================= Corregir uno solo =================

    [Fact]
    public async Task Corregir_QuitaUnSoloMedicamento_YDejaLosDemasIntactos()
    {
        await IndicarAsync(_pacienteId,
            ("Acetaminofén", "500 mg"), ("Amoxicilina", "500 mg"), ("Enjuague", "15 ml"));

        var antes = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        var quedan = antes.Medicamentos.Where(m => m.Medicamento != "Amoxicilina").ToList();

        await _indicaciones.ActualizarMedicamentosAsync(antes, quedan);

        var despues = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        despues.Id.Should().Be(antes.Id, "se corrige la misma indicación, no se crea otra");
        despues.Medicamentos.Select(m => m.Medicamento)
            .Should().Equal("Acetaminofén", "Enjuague");
        despues.Medicamentos[0].Dosis.Should().Be("500 mg", "lo que no se tocó no cambia");
    }

    [Fact]
    public async Task Corregir_CambiaLaDosisYAgregaOtro()
    {
        await IndicarAsync(_pacienteId, ("Acetaminofén", "500 mg"));

        var original = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        var nuevos = new List<IndicacionMedicamento>
        {
            new() { Medicamento = "Acetaminofén", Dosis = "650 mg", Frecuencia = "cada 8 horas" },
            new() { Medicamento = "Enjuague", Dosis = "15 ml" }
        };

        await _indicaciones.ActualizarMedicamentosAsync(original, nuevos);

        var despues = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        despues.Medicamentos.Should().HaveCount(2);
        despues.Medicamentos[0].Dosis.Should().Be("650 mg");
        despues.Medicamentos[0].Frecuencia.Should().Be("cada 8 horas");
    }

    [Fact]
    public async Task Corregir_DejaElAntesYElDespuesEnAuditoria()
    {
        // Sin esto, una corrección legítima sería indistinguible de borrar lo
        // que molestaba. Es la razón por la que se permite corregir.
        await IndicarAsync(_pacienteId, ("Acetaminofén", "500 mg"), ("Amoxicilina", "500 mg"));

        var original = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        await _indicaciones.ActualizarMedicamentosAsync(original,
            original.Medicamentos.Where(m => m.Medicamento == "Acetaminofén").ToList());

        var descripcion = await EscalarAsync("""
            SELECT descripcion FROM auditoria
            WHERE entidad = 'indicacion' AND accion = 'modificar'
            ORDER BY id DESC LIMIT 1;
            """);

        descripcion.Should().NotBeNull();
        descripcion.Should().Contain("Amoxicilina", "el que se quitó tiene que quedar nombrado");
        descripcion.Should().Contain("Antes:");
        descripcion.Should().Contain("Ahora:");
    }

    [Fact]
    public async Task Corregir_SinNingunMedicamento_SeNiega()
    {
        // Dejar la indicación sin medicamentos sería darla de baja por la puerta
        // de atrás, sin el motivo que exige la baja.
        await IndicarAsync(_pacienteId, ("Acetaminofén", "500 mg"));
        var original = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();

        await FluentActions
            .Awaiting(() => _indicaciones.ActualizarMedicamentosAsync(original, []))
            .Should().ThrowAsync<ArgumentException>();

        var despues = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        despues.Medicamentos.Should().ContainSingle("no se tocó nada");
    }

    [Fact]
    public async Task Corregir_DeUnDiaAnterior_SoloElAdmin()
    {
        var id = await IndicarAsync(_pacienteId, ("Acetaminofén", "500 mg"));
        await EnvejecerAsync(id, dias: 3);

        var original = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        var nuevos = new List<IndicacionMedicamento>
        {
            new() { Medicamento = "Acetaminofén", Dosis = "650 mg" }
        };

        EntrarComo("Servicio");
        await FluentActions
            .Awaiting(() => _indicaciones.ActualizarMedicamentosAsync(original, nuevos))
            .Should().ThrowAsync<UnauthorizedAccessException>();

        EntrarComo("Admin");
        await _indicaciones.ActualizarMedicamentosAsync(original, nuevos);

        var despues = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        despues.Medicamentos[0].Dosis.Should().Be("650 mg");
    }

    [Fact]
    public async Task Corregir_LoDeHoy_LoPuedeHacerElMostrador()
    {
        await IndicarAsync(_pacienteId, ("Acetaminofén", "500 mg"));
        var original = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();

        EntrarComo("Servicio");
        await _indicaciones.ActualizarMedicamentosAsync(original,
            [new IndicacionMedicamento { Medicamento = "Acetaminofén", Dosis = "650 mg" }]);

        var despues = (await _indicaciones.ObtenerDeClienteAsync(_pacienteId)).Single();
        despues.Medicamentos[0].Dosis.Should().Be("650 mg",
            "es el renglón que se acaba de tipear mal");
    }

    // =========================================================

    private async Task<long> IndicarAsync(long pacienteId,
        params (string Medicamento, string Dosis)[] medicamentos)
    {
        return await _indicaciones.CrearAsync(new Indicacion
        {
            ClienteId = pacienteId,
            ClienteNombre = "Paciente de prueba",
            MedicoId = _medicoId,
            MedicoNombre = "Dra. Prueba",
            Medicamentos = medicamentos
                .Select(m => new IndicacionMedicamento
                {
                    Medicamento = m.Medicamento,
                    Dosis = m.Dosis
                })
                .ToList()
        });
    }

    /// <summary>Mueve una indicación al pasado para probar la regla del día.</summary>
    private async Task EnvejecerAsync(long id, int dias)
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            "UPDATE indicacion SET fecha_utc = DATE_SUB(fecha_utc, INTERVAL @dias DAY) WHERE id = @id;";
        cmd.Parameters.AddWithValue("@dias", dias);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private void EntrarComo(string rol, params string[] permisosExtra)
    {
        SesionActual.Cerrar();
        var permisos = permisosExtra.Length > 0
            ? permisosExtra
            : ["indicaciones", "clientes", "medicos"];
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
