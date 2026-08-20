using System.Globalization;
using MED100.Common;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Reglas de la agenda: cuándo se puede poner una cita y qué huecos quedan
/// libres. Es cálculo puro — entran los horarios del médico, las citas que ya
/// tiene y la cita propuesta, y sale un veredicto. No toca la base ni el reloj
/// por su cuenta: el "ahora" se pasa por parámetro para poder probarlo sin
/// depender de la hora a la que corran las pruebas.
///
/// <b>Todo acá es HORA LOCAL de RD.</b> Los horarios del médico son locales
/// ("atiende de 8 a 12"), y las citas llegan ya convertidas. Mezclar UTC en
/// este archivo correría la agenda cuatro horas y el error se vería tarde,
/// cuando alguien llegue a una cita que el sistema puso de madrugada.
/// </summary>
public static class AgendaMedico
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    /// <summary>
    /// Paso de la grilla de huecos: 15 minutos. Ofrecer huecos minuto a minuto
    /// daría una lista ilegible, y de media hora dejaría fuera los "9:15" que
    /// en la práctica se usan todo el tiempo.
    /// </summary>
    public const int PasoMinutos = 15;

    /// <summary>Duración mínima y máxima aceptadas para una cita.</summary>
    public const int DuracionMinima = 5;
    public const int DuracionMaxima = 8 * 60;

    /// <summary>
    /// Valida una cita contra el horario del médico y contra sus otras citas.
    /// Lanza <see cref="ArgumentException"/> con un mensaje que se le puede
    /// mostrar tal cual a la recepcionista.
    /// </summary>
    /// <param name="inicioLocal">Comienzo de la cita, en hora local de RD.</param>
    /// <param name="duracionMinutos">Cuánto ocupa.</param>
    /// <param name="horarios">TODOS los tramos del médico (se filtra por día acá).</param>
    /// <param name="citasDelMedico">Citas ya existentes del médico (cualquier día).</param>
    /// <param name="exceptoCitaId">Al editar, la propia cita no cuenta como choque.</param>
    /// <param name="ahoraLocal">Momento actual; por defecto, el reloj del negocio.</param>
    public static void Validar(
        DateTime inicioLocal,
        int duracionMinutos,
        IEnumerable<MedicoHorario> horarios,
        IEnumerable<Cita> citasDelMedico,
        long? exceptoCitaId = null,
        DateTime? ahoraLocal = null)
    {
        if (duracionMinutos < DuracionMinima)
            throw new ArgumentException($"La cita tiene que durar al menos {DuracionMinima} minutos.");
        if (duracionMinutos > DuracionMaxima)
            throw new ArgumentException(
                $"La cita no puede durar más de {DuracionMaxima / 60} horas. " +
                "Si de verdad lleva todo el día, cargala como varias citas.");

        var ahora = ahoraLocal ?? FechaNegocio.AhoraLocal();
        if (inicioLocal < ahora)
            throw new ArgumentException(
                "Esa hora ya pasó. Para dejar constancia de una cita vieja, cargala y marcala como atendida.");

        var fin = inicioLocal.AddMinutes(duracionMinutos);

        // 1) ¿Cae dentro de un tramo del médico ese día de la semana?
        var dia = DisponibilidadMedicos.DiaSemanaDe(inicioLocal);
        var tramosDelDia = horarios.Where(h => h.DiaSemana == dia)
                                   .OrderBy(h => h.HoraInicio)
                                   .ToList();

        if (tramosDelDia.Count == 0)
            throw new ArgumentException(
                $"El médico no atiende los {DisponibilidadMedicos.NombreDia(dia).ToLower(CulturaRd)}.");

        var inicioHora = TimeOnly.FromDateTime(inicioLocal);
        // La cita tiene que caber ENTERA en un solo tramo. Si el médico atiende
        // de 8 a 12 y de 2 a 6, una cita de 11:45 que dura una hora no "sigue"
        // después del almuerzo: no cabe.
        var cabe = tramosDelDia.Any(t =>
            inicioHora >= t.HoraInicio &&
            MinutosDesde(t.HoraInicio, inicioHora) + duracionMinutos <= t.DuracionMinutos);

        if (!cabe)
            throw new ArgumentException(
                $"A esa hora el médico no atiende. Los {DisponibilidadMedicos.NombreDia(dia).ToLower(CulturaRd)} " +
                $"atiende {DescribirTramos(tramosDelDia)}.");

        // 2) ¿Se pisa con otra cita suya?
        var choque = citasDelMedico.FirstOrDefault(c =>
            c.Id != exceptoCitaId &&
            c.OcupaAgenda &&
            Solapan(inicioLocal, fin, LocalDe(c), FinLocalDe(c)));

        if (choque is not null)
            throw new ArgumentException(
                $"El médico ya tiene una cita de {Formatear(LocalDe(choque))} a " +
                $"{Formatear(FinLocalDe(choque))} con {choque.PacienteNombre}.");
    }

    /// <summary>
    /// Huecos libres de un médico en un día, para la duración pedida.
    ///
    /// Es lo que convierte la pantalla en algo usable: en vez de que la
    /// recepcionista adivine una hora y el sistema le diga que no, se le
    /// ofrecen las que sí se pueden.
    /// </summary>
    public static IReadOnlyList<HuecoAgenda> HuecosLibres(
        DateOnly dia,
        int duracionMinutos,
        IEnumerable<MedicoHorario> horarios,
        IEnumerable<Cita> citasDelMedico,
        long? exceptoCitaId = null,
        DateTime? ahoraLocal = null)
    {
        if (duracionMinutos < DuracionMinima)
            return [];

        var ahora = ahoraLocal ?? FechaNegocio.AhoraLocal();
        var diaSemana = DisponibilidadMedicos.DiaSemanaDe(dia.ToDateTime(TimeOnly.MinValue));

        var ocupadas = citasDelMedico
            .Where(c => c.Id != exceptoCitaId && c.OcupaAgenda)
            .Select(c => (Inicio: LocalDe(c), Fin: FinLocalDe(c)))
            .ToList();

        var huecos = new List<HuecoAgenda>();

        foreach (var tramo in horarios.Where(h => h.DiaSemana == diaSemana).OrderBy(h => h.HoraInicio))
        {
            var cursor = dia.ToDateTime(tramo.HoraInicio);
            var finTramo = cursor.AddMinutes(tramo.DuracionMinutos);

            while (cursor.AddMinutes(duracionMinutos) <= finTramo)
            {
                var finCita = cursor.AddMinutes(duracionMinutos);
                var libre = cursor >= ahora &&
                            !ocupadas.Any(o => Solapan(cursor, finCita, o.Inicio, o.Fin));

                if (libre)
                    huecos.Add(new HuecoAgenda(cursor, duracionMinutos));

                cursor = cursor.AddMinutes(PasoMinutos);
            }
        }

        return huecos;
    }

    /// <summary>Etiqueta en español del estado, para la UI y la auditoría.</summary>
    public static string EtiquetaEstado(EstadoCita estado) => estado switch
    {
        EstadoCita.Programada => "Programada",
        EstadoCita.Confirmada => "Confirmada",
        EstadoCita.Atendida => "Atendida",
        EstadoCita.Cancelada => "Cancelada",
        EstadoCita.NoAsistio => "No asistió",
        _ => estado.ToString()
    };

    /// <summary>
    /// Qué transiciones de estado tienen sentido. Una cita atendida no vuelve a
    /// "programada": ya pasó. Se deja explícito para que la UI ofrezca solo lo
    /// que se puede y no haya que descubrirlo con un error.
    /// </summary>
    public static IReadOnlyList<EstadoCita> SiguientesEstados(EstadoCita actual) => actual switch
    {
        EstadoCita.Programada =>
            [EstadoCita.Confirmada, EstadoCita.Atendida, EstadoCita.Cancelada, EstadoCita.NoAsistio],
        EstadoCita.Confirmada =>
            [EstadoCita.Atendida, EstadoCita.Cancelada, EstadoCita.NoAsistio],
        // Terminales: lo que ya ocurrió, ocurrió.
        _ => []
    };

    public static bool EsEstadoFinal(EstadoCita estado) =>
        estado is EstadoCita.Atendida or EstadoCita.Cancelada or EstadoCita.NoAsistio;

    // ---------- Auxiliares ----------

    private static DateTime LocalDe(Cita cita) => FechaNegocio.AUtcLocal(cita.FechaHoraUtc);

    private static DateTime FinLocalDe(Cita cita) => LocalDe(cita).AddMinutes(cita.DuracionMinutos);

    /// <summary>
    /// Dos rangos se pisan si cada uno empieza antes de que termine el otro.
    /// El fin es EXCLUSIVO: una cita de 9 a 10 y otra de 10 a 11 NO se pisan.
    /// </summary>
    private static bool Solapan(DateTime inicioA, DateTime finA, DateTime inicioB, DateTime finB) =>
        inicioA < finB && inicioB < finA;

    private static int MinutosDesde(TimeOnly desde, TimeOnly hasta) =>
        (int)(hasta - desde).TotalMinutes;

    private static string Formatear(DateTime momento) => momento.ToString("h:mm tt", CulturaRd);

    private static string DescribirTramos(IEnumerable<MedicoHorario> tramos) =>
        string.Join(" y ", tramos.Select(t =>
            $"de {t.HoraInicio.ToString("h:mm tt", CulturaRd)} a {t.HoraFin.ToString("h:mm tt", CulturaRd)}"));
}
