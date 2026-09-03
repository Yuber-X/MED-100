using FluentAssertions;
using MED100.Common;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Reglas de la agenda. Todo con "ahora" explícito: si dependiera del reloj,
/// estos tests fallarían solos según la hora a la que corran.
///
/// El médico de referencia atiende los MARTES de 8:00 a 12:00 y de 14:00 a
/// 18:00 — el corte del almuerzo es justamente lo que hace interesante el caso.
/// </summary>
public class AgendaMedicoTests
{
    // Martes 18 de agosto de 2026
    private static readonly DateOnly Martes = new(2026, 8, 18);
    private static readonly DateTime AhoraTemprano = Martes.ToDateTime(new TimeOnly(7, 0));

    private static List<MedicoHorario> HorarioPartido() =>
    [
        new() { Id = 1, MedicoId = 1, DiaSemana = 3, HoraInicio = new(8, 0), HoraFin = new(12, 0) },
        new() { Id = 2, MedicoId = 1, DiaSemana = 3, HoraInicio = new(14, 0), HoraFin = new(18, 0) }
    ];

    private static DateTime EnMartes(int hora, int minuto = 0) =>
        Martes.ToDateTime(new TimeOnly(hora, minuto));

    /// <summary>Cita existente. La hora se da en LOCAL y se guarda como UTC, igual que en la base.</summary>
    private static Cita CitaEn(DateTime inicioLocal, int duracion = 30,
        EstadoCita estado = EstadoCita.Programada, long id = 100) => new()
        {
            Id = id,
            MedicoId = 1,
            PacienteNombre = "Paciente Existente",
            FechaHoraUtc = FechaNegocio.ALocalUtc(inicioLocal),
            DuracionMinutos = duracion,
            Estado = estado
        };

    private static Action Validar(DateTime inicio, int duracion = 30,
        IEnumerable<Cita>? citas = null, long? excepto = null) =>
        () => AgendaMedico.Validar(inicio, duracion, HorarioPartido(), citas ?? [],
            excepto, AhoraTemprano);

    // =========================================================
    // Horario del médico
    // =========================================================

    [Fact]
    public void Validar_DentroDelTramoDeLaManana_Pasa() =>
        Validar(EnMartes(9)).Should().NotThrow();

    [Fact]
    public void Validar_DentroDelTramoDeLaTarde_Pasa() =>
        Validar(EnMartes(15, 30)).Should().NotThrow();

    [Fact]
    public void Validar_JustoAlAbrir_Pasa() =>
        Validar(EnMartes(8)).Should().NotThrow();

    [Fact]
    public void Validar_TerminaJustoAlCerrar_Pasa()
    {
        // De 11:30 a 12:00 cabe: el fin del tramo es exclusivo pero la cita
        // termina exactamente ahí, no lo pasa.
        Validar(EnMartes(11, 30)).Should().NotThrow();
    }

    [Fact]
    public void Validar_EnElAlmuerzo_SeNiega()
    {
        Validar(EnMartes(13)).Should().Throw<ArgumentException>()
            .WithMessage("*no atiende*");
    }

    [Fact]
    public void Validar_QueSeDesbordaDelTramo_SeNiega()
    {
        // 11:45 + 60 min se pasaría de las 12:00. La cita NO "sigue" después
        // del almuerzo: tiene que caber entera en un solo tramo.
        Validar(EnMartes(11, 45), duracion: 60).Should().Throw<ArgumentException>()
            .WithMessage("*no atiende*");
    }

    [Fact]
    public void Validar_UnDiaQueNoAtiende_SeNiega()
    {
        // Miércoles 19: el médico solo tiene horario los martes.
        var miercoles = new DateOnly(2026, 8, 19).ToDateTime(new TimeOnly(9, 0));
        var accion = () => AgendaMedico.Validar(miercoles, 30, HorarioPartido(), [],
            null, AhoraTemprano);

        accion.Should().Throw<ArgumentException>().WithMessage("*no atiende los miércoles*");
    }

    [Fact]
    public void Validar_MedicoSinHorarios_SeNiega()
    {
        var accion = () => AgendaMedico.Validar(EnMartes(9), 30, [], [], null, AhoraTemprano);
        accion.Should().Throw<ArgumentException>().WithMessage("*no atiende*");
    }

    // =========================================================
    // Choques con otras citas
    // =========================================================

    [Fact]
    public void Validar_SobreOtraCita_SeNiega()
    {
        var existente = CitaEn(EnMartes(9), 30);

        Validar(EnMartes(9, 15), citas: [existente]).Should().Throw<ArgumentException>()
            .WithMessage("*ya tiene una cita*");
    }

    [Fact]
    public void Validar_PegadaAOtraCita_Pasa()
    {
        // De 9:00 a 9:30 y de 9:30 a 10:00 NO se pisan: el fin es exclusivo.
        var existente = CitaEn(EnMartes(9), 30);

        Validar(EnMartes(9, 30), citas: [existente]).Should().NotThrow();
    }

    [Fact]
    public void Validar_QueEnvuelveAOtraCita_SeNiega()
    {
        var existente = CitaEn(EnMartes(9, 30), 30);

        Validar(EnMartes(9), duracion: 120, citas: [existente]).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(EstadoCita.Cancelada)]
    [InlineData(EstadoCita.NoAsistio)]
    public void Validar_SobreUnaCitaCanceladaOSinAsistir_Pasa(EstadoCita estado)
    {
        // El hueco quedó libre: se le puede dar a otro paciente.
        var liberada = CitaEn(EnMartes(9), 30, estado);

        Validar(EnMartes(9), citas: [liberada]).Should().NotThrow();
    }

    [Fact]
    public void Validar_AlEditarLaMismaCita_NoChocaConsigoMisma()
    {
        var propia = CitaEn(EnMartes(9), 30, id: 55);

        Validar(EnMartes(9), citas: [propia], excepto: 55).Should().NotThrow();
    }

    // =========================================================
    // Tiempo y duración
    // =========================================================

    [Fact]
    public void Validar_EnElPasado_SeNiega()
    {
        var ahora = Martes.ToDateTime(new TimeOnly(10, 0));
        var accion = () => AgendaMedico.Validar(EnMartes(9), 30, HorarioPartido(), [], null, ahora);

        accion.Should().Throw<ArgumentException>().WithMessage("*ya pasó*");
    }

    [Fact]
    public void Validar_DuracionCero_SeNiega() =>
        Validar(EnMartes(9), duracion: 0).Should().Throw<ArgumentException>()
            .WithMessage("*al menos*");

    [Fact]
    public void Validar_DuracionAbsurda_SeNiega() =>
        Validar(EnMartes(9), duracion: 10 * 60).Should().Throw<ArgumentException>()
            .WithMessage("*no puede durar más*");

    // =========================================================
    // Huecos libres
    // =========================================================

    [Fact]
    public void Huecos_DiaVacio_CubreLosDosTramos()
    {
        var huecos = AgendaMedico.HuecosLibres(Martes, 30, HorarioPartido(), [], null, AhoraTemprano);

        // Mañana 8:00–12:00 → últimos 30 min arrancan 11:30 → 15 huecos.
        // Tarde 14:00–18:00 → otros 15. Total 30.
        huecos.Should().HaveCount(30);
        huecos.First().InicioLocal.Should().Be(EnMartes(8));
        huecos.Last().InicioLocal.Should().Be(EnMartes(17, 30));
    }

    [Fact]
    public void Huecos_NingunoCaeEnElAlmuerzo()
    {
        var huecos = AgendaMedico.HuecosLibres(Martes, 30, HorarioPartido(), [], null, AhoraTemprano);

        huecos.Should().NotContain(h => h.InicioLocal.Hour == 12 || h.InicioLocal.Hour == 13);
    }

    [Fact]
    public void Huecos_ConUnaCitaPuesta_LaSacaYLosQueLaPisan()
    {
        var existente = CitaEn(EnMartes(9), 30);

        var huecos = AgendaMedico.HuecosLibres(Martes, 30, HorarioPartido(), [existente],
            null, AhoraTemprano);

        // Se pisan con la cita de 9:00–9:30 los arranques 8:45, 9:00 y 9:15.
        var inicios = huecos.Select(h => h.InicioLocal).ToList();
        inicios.Should().NotContain(EnMartes(8, 45));
        inicios.Should().NotContain(EnMartes(9, 0));
        inicios.Should().NotContain(EnMartes(9, 15));
        inicios.Should().Contain(EnMartes(8, 30));
        inicios.Should().Contain(EnMartes(9, 30));
    }

    [Fact]
    public void Huecos_NoOfreceHorasQueYaPasaron()
    {
        var ahora = Martes.ToDateTime(new TimeOnly(15, 0));

        var huecos = AgendaMedico.HuecosLibres(Martes, 30, HorarioPartido(), [], null, ahora);

        huecos.Should().OnlyContain(h => h.InicioLocal >= ahora);
        huecos.First().InicioLocal.Should().Be(EnMartes(15));
    }

    [Fact]
    public void Huecos_DuracionLarga_SoloDondeCabeEntera()
    {
        // Dos horas: en un tramo de cuatro, el último arranque posible es a las 10.
        var huecos = AgendaMedico.HuecosLibres(Martes, 120, HorarioPartido(), [], null, AhoraTemprano);

        var inicios = huecos.Select(h => h.InicioLocal).ToList();
        inicios.Should().Contain(EnMartes(10, 0));
        inicios.Should().NotContain(EnMartes(10, 15));
        inicios.Should().NotContain(EnMartes(16, 15));
    }

    [Fact]
    public void Huecos_DiaSinHorario_DevuelveVacio()
    {
        var domingo = new DateOnly(2026, 8, 23);

        AgendaMedico.HuecosLibres(domingo, 30, HorarioPartido(), [], null, AhoraTemprano)
            .Should().BeEmpty();
    }

    [Fact]
    public void Huecos_LosQueSalenSonTodosValidos()
    {
        // Propiedad: si el cálculo lo ofrece, la validación tiene que aceptarlo.
        // Es lo que impide que la pantalla ofrezca una hora que después rebota.
        var existentes = new List<Cita>
        {
            CitaEn(EnMartes(9), 45, id: 1),
            CitaEn(EnMartes(14, 30), 60, id: 2),
            CitaEn(EnMartes(16), 30, EstadoCita.Cancelada, id: 3)
        };

        var huecos = AgendaMedico.HuecosLibres(Martes, 30, HorarioPartido(), existentes,
            null, AhoraTemprano);

        huecos.Should().NotBeEmpty();
        foreach (var hueco in huecos)
        {
            var accion = () => AgendaMedico.Validar(hueco.InicioLocal, 30, HorarioPartido(),
                existentes, null, AhoraTemprano);
            accion.Should().NotThrow($"el hueco de las {hueco.HoraTexto} se ofreció como libre");
        }
    }

    // =========================================================
    // Estados
    // =========================================================

    [Fact]
    public void Estados_UnaCitaProgramadaPuedeIrACualquiera()
    {
        AgendaMedico.SiguientesEstados(EstadoCita.Programada).Should().BeEquivalentTo(
            [EstadoCita.Confirmada, EstadoCita.Atendida, EstadoCita.Cancelada, EstadoCita.NoAsistio]);
    }

    [Fact]
    public void Estados_UnaConfirmadaYaNoVuelveAProgramada()
    {
        AgendaMedico.SiguientesEstados(EstadoCita.Confirmada)
            .Should().NotContain(EstadoCita.Programada);
    }

    [Theory]
    [InlineData(EstadoCita.Atendida)]
    [InlineData(EstadoCita.Cancelada)]
    [InlineData(EstadoCita.NoAsistio)]
    public void Estados_LosFinalesNoTienenSalida(EstadoCita estado)
    {
        AgendaMedico.SiguientesEstados(estado).Should().BeEmpty();
        AgendaMedico.EsEstadoFinal(estado).Should().BeTrue();
    }

    // =========================================================
    // Deshacer un estado puesto por error (pedido 2026-08-28)
    // =========================================================

    /// <summary>
    /// La vuelta atrás existe justamente porque los estados finales no tienen
    /// salida: sin esto, un clic de más dejaba la cita muerta.
    /// </summary>
    [Theory]
    [InlineData(EstadoCita.Atendida)]
    [InlineData(EstadoCita.Cancelada)]
    [InlineData(EstadoCita.NoAsistio)]
    public void Deshacer_UnEstadoFinalVuelveAProgramada(EstadoCita estado) =>
        AgendaMedico.EstadoAlDeshacer(estado).Should().Be(EstadoCita.Programada);

    /// <summary>
    /// Vuelve a Programada y NO a Confirmada: pasar a confirmada afirmaría que
    /// el paciente reconfirmó, y eso no lo sabe nadie. Es la diferencia entre
    /// corregir una marca y inventar un hecho.
    /// </summary>
    [Fact]
    public void Deshacer_NuncaDevuelveAConfirmada() =>
        AgendaMedico.EstadoAlDeshacer(EstadoCita.Atendida)
            .Should().NotBe(EstadoCita.Confirmada);

    [Theory]
    [InlineData(EstadoCita.Programada)]
    [InlineData(EstadoCita.Confirmada)]
    public void Deshacer_UnaCitaEnCursoNoTieneNadaQueDeshacer(EstadoCita estado) =>
        AgendaMedico.EstadoAlDeshacer(estado).Should().BeNull();

    // =========================================================
    // ChoqueDeAgenda: la pregunta que hace falta al reabrir
    // =========================================================

    /// <summary>
    /// Reabrir una cita cancelada la vuelve a poner en la agenda. Si mientras
    /// estuvo cancelada alguien tomó ese lugar, hay que verlo AHORA y no el día
    /// de la consulta con los dos pacientes sentados en la sala.
    /// </summary>
    [Fact]
    public void Choque_DetectaAlQueSeMetioEnElHuecoLiberado()
    {
        var otro = CitaEn(EnMartes(9), duracion: 30, id: 200);
        AgendaMedico.ChoqueDeAgenda(EnMartes(9, 15), 30, [otro], exceptoCitaId: 100)
            .Should().Be(otro);
    }

    [Fact]
    public void Choque_ConElHuecoLibreDaNull() =>
        AgendaMedico.ChoqueDeAgenda(EnMartes(11), 30,
            [CitaEn(EnMartes(9), id: 200)], exceptoCitaId: 100).Should().BeNull();

    /// <summary>
    /// Una cita cancelada NO ocupa la agenda, así que no puede impedir que otra
    /// se reabra en su misma hora. Sin esto, cancelar dos veces la misma franja
    /// la dejaría bloqueada para siempre.
    /// </summary>
    [Fact]
    public void Choque_LasCanceladasNoBloquean() =>
        AgendaMedico.ChoqueDeAgenda(EnMartes(9), 30,
            [CitaEn(EnMartes(9), estado: EstadoCita.Cancelada, id: 200)]).Should().BeNull();

    /// <summary>La cita que se está reabriendo no choca consigo misma.</summary>
    [Fact]
    public void Choque_LaPropiaCitaNoCuenta() =>
        AgendaMedico.ChoqueDeAgenda(EnMartes(9), 30,
            [CitaEn(EnMartes(9), id: 100)], exceptoCitaId: 100).Should().BeNull();

    /// <summary>
    /// A diferencia de Validar, esto NO exige que la hora sea futura: se usa
    /// para reabrir citas viejas mal marcadas, que por definición ya pasaron.
    /// </summary>
    [Fact]
    public void Choque_NoLeImportaQueLaHoraYaHayaPasado() =>
        AgendaMedico.ChoqueDeAgenda(new DateTime(2020, 1, 7, 9, 0, 0), 30, [])
            .Should().BeNull();
}
