using FluentAssertions;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// "¿Quién está disponible en este momento?" — la pregunta que la recepción
/// hace todo el día (pedido del cliente 2026-08-10).
///
/// El momento se inyecta en todas las pruebas: si dependieran del reloj real,
/// pasarían de mañana y fallarían de noche.
/// </summary>
public class DisponibilidadMedicosTests
{
    // Miércoles 12 de agosto de 2026. DAYOFWEEK() = 4.
    private static readonly DateTime Miercoles = new(2026, 8, 12);

    private static Medico Doctor(long id = 1, string nombre = "Dra. Peña", bool activo = true) =>
        new() { Id = id, Nombre = nombre, Activo = activo };

    private static MedicoHorario Tramo(int dia, string desde, string hasta, long medicoId = 1, long id = 0) =>
        new()
        {
            Id = id,
            MedicoId = medicoId,
            DiaSemana = dia,
            HoraInicio = TimeOnly.Parse(desde),
            HoraFin = TimeOnly.Parse(hasta)
        };

    private static DateTime AlaHora(string hora) => Miercoles.Add(TimeOnly.Parse(hora).ToTimeSpan());

    // =========================================================
    // El día de la semana: la convención tiene que ser la de MySQL
    // =========================================================

    /// <summary>
    /// En .NET, domingo vale 0; en MySQL DAYOFWEEK(), 1. Los horarios se
    /// guardan con la convención de MySQL, así que si acá se usara la de .NET
    /// todos los horarios aparecerían corridos un día — y eso se descubre
    /// tarde y mal.
    /// </summary>
    [Theory]
    [InlineData(2026, 8, 9, 1)]   // domingo
    [InlineData(2026, 8, 10, 2)]  // lunes
    [InlineData(2026, 8, 12, 4)]  // miércoles
    [InlineData(2026, 8, 15, 7)]  // sábado
    public void DiaSemana_UsaLaConvencionDeMySql(int a, int m, int d, int esperado)
    {
        DisponibilidadMedicos.DiaSemanaDe(new DateTime(a, m, d)).Should().Be(esperado);
    }

    [Fact]
    public void NombreDia_CubreLaSemanaYRechazaLoQueNoEs()
    {
        DisponibilidadMedicos.NombreDia(1).Should().Be("Domingo");
        DisponibilidadMedicos.NombreDia(7).Should().Be("Sábado");
        var accion = () => DisponibilidadMedicos.NombreDia(0);
        accion.Should().Throw<ArgumentOutOfRangeException>();
    }

    // =========================================================
    // Estado de un médico
    // =========================================================

    [Fact]
    public void DentroDelTramo_AtiendeYDiceHastaCuando()
    {
        var horarios = new[] { Tramo(4, "08:00", "12:00") };

        var d = DisponibilidadMedicos.Evaluar(Doctor(), horarios, AlaHora("09:30"));

        d.AtiendeAhora.Should().BeTrue();
        d.Tramo.Should().NotBeNull();
        d.Detalle.Should().Contain("12:00");
    }

    /// <summary>
    /// A las 12:00 en punto un tramo de 8 a 12 YA TERMINÓ. El fin es
    /// exclusivo porque es como lo entiende la gente: "atiende hasta las 12"
    /// significa que a las 12 ya no está.
    /// </summary>
    [Fact]
    public void JustoAlaHoraDeSalida_YaNoAtiende()
    {
        var horarios = new[] { Tramo(4, "08:00", "12:00") };

        DisponibilidadMedicos.Evaluar(Doctor(), horarios, AlaHora("12:00"))
            .AtiendeAhora.Should().BeFalse();
    }

    [Fact]
    public void JustoAlaHoraDeEntrada_YaAtiende()
    {
        var horarios = new[] { Tramo(4, "08:00", "12:00") };

        DisponibilidadMedicos.Evaluar(Doctor(), horarios, AlaHora("08:00"))
            .AtiendeAhora.Should().BeTrue();
    }

    /// <summary>El caso real: mañana y tarde con el corte del almuerzo en el medio.</summary>
    [Fact]
    public void EnElAlmuerzo_NoAtiendeYAvisaCuandoVuelve()
    {
        var horarios = new[] { Tramo(4, "08:00", "12:00"), Tramo(4, "14:00", "18:00") };

        var d = DisponibilidadMedicos.Evaluar(Doctor(), horarios, AlaHora("13:00"));

        d.AtiendeAhora.Should().BeFalse();
        d.Detalle.Should().Contain("2:00");
        d.Tramo!.HoraInicio.Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public void DespuesDelUltimoTramo_DiceQueYaTermino()
    {
        var horarios = new[] { Tramo(4, "08:00", "12:00"), Tramo(4, "14:00", "18:00") };

        var d = DisponibilidadMedicos.Evaluar(Doctor(), horarios, AlaHora("19:00"));

        d.AtiendeAhora.Should().BeFalse();
        d.Tramo.Should().BeNull();
        d.Detalle.Should().Be("Ya terminó por hoy");
    }

    [Fact]
    public void SinTramosEseDia_DiceQueHoyNoAtiende()
    {
        // Solo atiende los lunes (2); se evalúa un miércoles
        var horarios = new[] { Tramo(2, "08:00", "12:00") };

        var d = DisponibilidadMedicos.Evaluar(Doctor(), horarios, AlaHora("09:00"));

        d.AtiendeAhora.Should().BeFalse();
        d.Detalle.Should().Be("Hoy no atiende");
    }

    /// <summary>
    /// Un médico se desactiva justamente para sacarlo de la lista sin borrarle
    /// el historial de facturas. Si siguiera apareciendo disponible, no habría
    /// servido de nada.
    /// </summary>
    [Fact]
    public void MedicoInactivo_NoAtiendeAunqueTengaHorario()
    {
        var horarios = new[] { Tramo(4, "08:00", "12:00") };

        var d = DisponibilidadMedicos.Evaluar(Doctor(activo: false), horarios, AlaHora("09:30"));

        d.AtiendeAhora.Should().BeFalse();
        d.Detalle.Should().Be("Inactivo");
    }

    [Fact]
    public void MedicoSinHorariosCargados_LoDiceAsi()
    {
        // "Hoy no atiende" y "nunca se le cargó el horario" son problemas
        // distintos: el segundo se arregla en la pantalla de Médicos, y decirle
        // lo mismo a los dos manda a la recepción a buscar donde no es.
        var d = DisponibilidadMedicos.Evaluar(Doctor(), [], AlaHora("09:30"));

        d.AtiendeAhora.Should().BeFalse();
        d.Detalle.Should().Be("Sin horario cargado");
        d.DiasQueAtiende.Should().BeEmpty();
    }

    [Fact]
    public void MedicoConHorarioOtroDia_DiceQueHoyNoAtiende()
    {
        // Sí tiene horario, pero no hoy: eso es "hoy no atiende".
        var d = DisponibilidadMedicos.Evaluar(Doctor(), [Tramo(5, "08:00", "12:00")],
            AlaHora("09:30"));

        d.AtiendeAhora.Should().BeFalse();
        d.Detalle.Should().Be("Hoy no atiende");
        d.DiasQueAtiende.Should().Equal([5]);
    }

    // =========================================================
    // La lista completa
    // =========================================================

    [Fact]
    public void LaLista_PoneAdelanteALosQueAtiendenYLuegoOrdenaPorNombre()
    {
        var medicos = new[]
        {
            Doctor(1, "Dr. Zapata"),
            Doctor(2, "Dra. Almonte"),
            Doctor(3, "Dr. Batista")
        };
        var horarios = new[]
        {
            Tramo(4, "08:00", "12:00", medicoId: 1),   // Zapata: atiende
            Tramo(4, "14:00", "18:00", medicoId: 2),   // Almonte: entra después
            Tramo(4, "08:00", "12:00", medicoId: 3)    // Batista: atiende
        };

        var lista = DisponibilidadMedicos.Evaluar(medicos, horarios, AlaHora("09:00"));

        lista.Should().HaveCount(3);
        lista[0].Medico.Nombre.Should().Be("Dr. Batista");
        lista[1].Medico.Nombre.Should().Be("Dr. Zapata");
        lista[0].AtiendeAhora.Should().BeTrue();
        lista[1].AtiendeAhora.Should().BeTrue();
        lista[2].Medico.Nombre.Should().Be("Dra. Almonte");
        lista[2].AtiendeAhora.Should().BeFalse();
    }

    [Fact]
    public void LaLista_NoMezclaLosHorariosDeUnMedicoConLosDeOtro()
    {
        var medicos = new[] { Doctor(1, "Dr. Uno"), Doctor(2, "Dr. Dos") };
        // Solo el 1 tiene horario hoy
        var horarios = new[] { Tramo(4, "08:00", "12:00", medicoId: 1) };

        var lista = DisponibilidadMedicos.Evaluar(medicos, horarios, AlaHora("09:00"));

        lista.Single(d => d.Medico.Id == 1).AtiendeAhora.Should().BeTrue();
        lista.Single(d => d.Medico.Id == 2).AtiendeAhora.Should().BeFalse();
    }

    // =========================================================
    // Validación al guardar un tramo
    // =========================================================

    [Fact]
    public void TramoAlReves_SeRechaza()
    {
        var accion = () => DisponibilidadMedicos.ValidarTramo(
            new HorarioDatos(4, new TimeOnly(18, 0), new TimeOnly(8, 0)), []);

        accion.Should().Throw<ArgumentException>()
            .WithMessage("*posterior*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void DiaFueraDeRango_SeRechaza(int dia)
    {
        var accion = () => DisponibilidadMedicos.ValidarTramo(
            new HorarioDatos(dia, new TimeOnly(8, 0), new TimeOnly(12, 0)), []);

        accion.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Dos tramos superpuestos harían que el médico apareciera disponible por
    /// duplicado y que la agenda ofreciera el mismo hueco dos veces.
    /// </summary>
    [Theory]
    [InlineData("11:00", "13:00")]  // pisa el final
    [InlineData("07:00", "09:00")]  // pisa el principio
    [InlineData("09:00", "10:00")]  // queda adentro
    [InlineData("07:00", "13:00")]  // lo contiene entero
    public void TramoQueSePisaConOtro_SeRechaza(string desde, string hasta)
    {
        var existentes = new[] { Tramo(4, "08:00", "12:00", id: 5) };

        var accion = () => DisponibilidadMedicos.ValidarTramo(
            new HorarioDatos(4, TimeOnly.Parse(desde), TimeOnly.Parse(hasta)), existentes);

        accion.Should().Throw<ArgumentException>().WithMessage("*se pisa*");
    }

    [Fact]
    public void TramoPegadoAlAnterior_SeAcepta()
    {
        // De 12 a 14 justo después de 8 a 12: no se pisan, se tocan
        var existentes = new[] { Tramo(4, "08:00", "12:00", id: 5) };

        var accion = () => DisponibilidadMedicos.ValidarTramo(
            new HorarioDatos(4, new TimeOnly(12, 0), new TimeOnly(14, 0)), existentes);

        accion.Should().NotThrow();
    }

    [Fact]
    public void MismoHorarioEnOtroDia_SeAcepta()
    {
        var existentes = new[] { Tramo(4, "08:00", "12:00", id: 5) };

        var accion = () => DisponibilidadMedicos.ValidarTramo(
            new HorarioDatos(5, new TimeOnly(8, 0), new TimeOnly(12, 0)), existentes);

        accion.Should().NotThrow();
    }

    /// <summary>Al EDITAR un tramo no puede chocar consigo mismo.</summary>
    [Fact]
    public void EditarUnTramoSinMoverlo_NoChocaConsigoMismo()
    {
        var existentes = new[] { Tramo(4, "08:00", "12:00", id: 5) };

        var accion = () => DisponibilidadMedicos.ValidarTramo(
            new HorarioDatos(4, new TimeOnly(8, 0), new TimeOnly(13, 0)), existentes, exceptoId: 5);

        accion.Should().NotThrow();
    }
}
