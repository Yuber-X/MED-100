using System.Globalization;
using MED100.Common;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Contesta la pregunta de la recepción: <b>¿quién está atendiendo ahora?</b>
/// (pedido del cliente 2026-08-10: "sus horarios editables para saber quién
/// está disponible en el momento").
///
/// Es cálculo puro: entran los médicos con sus tramos y un momento, sale el
/// estado de cada uno. No toca la base ni el reloj por su cuenta — el momento
/// se pasa por parámetro para que se pueda probar sin depender de la hora a la
/// que corran las pruebas.
///
/// LA HORA ES LA LOCAL DEL NEGOCIO, no UTC. Un médico que atiende de 8 a 12
/// atiende de 8 a 12 en Santo Domingo; comparar contra UTC lo correría cuatro
/// horas y a media mañana diría que ya se fue.
/// </summary>
public static class DisponibilidadMedicos
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    /// <summary>Estado de cada médico en el momento indicado, ordenados: primero los que atienden.</summary>
    public static IReadOnlyList<MedicoDisponibilidad> Evaluar(
        IEnumerable<Medico> medicos,
        IEnumerable<MedicoHorario> horarios,
        DateTime momentoLocal)
    {
        var porMedico = horarios.GroupBy(h => h.MedicoId)
                                .ToDictionary(g => g.Key, g => g.ToList());

        return medicos
            .Select(m => Evaluar(m, porMedico.GetValueOrDefault(m.Id, []), momentoLocal))
            .OrderByDescending(d => d.AtiendeAhora)
            .ThenBy(d => d.Medico.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Estado de UN médico. Usa el reloj local del negocio si no se indica el momento.</summary>
    public static MedicoDisponibilidad Evaluar(Medico medico, IReadOnlyList<MedicoHorario> horarios,
        DateTime? momentoLocal = null)
    {
        var ahora = momentoLocal ?? FechaNegocio.AhoraLocal();

        // Los días fijos viajan siempre, atienda o no ahora mismo: en el
        // mostrador se pregunta "¿qué días viene?" mucho más que "¿está?".
        var dias = DiasQueAtiende(horarios);

        // Un médico inactivo no atiende aunque tenga horarios cargados: se
        // desactiva justamente para sacarlo de la lista sin borrarle el historial.
        if (!medico.Activo)
            return new MedicoDisponibilidad(medico, false, null, "Inactivo", dias);

        var dia = DiaSemanaDe(ahora);
        var hora = TimeOnly.FromDateTime(ahora);

        var deHoy = horarios.Where(h => h.DiaSemana == dia)
                            .OrderBy(h => h.HoraInicio)
                            .ToList();

        if (deHoy.Count == 0)
            return new MedicoDisponibilidad(medico, false, null,
                dias.Count == 0 ? "Sin horario cargado" : "Hoy no atiende", dias);

        // ¿Está dentro de algún tramo? El fin es EXCLUSIVO: a las 12:00 en punto
        // un tramo de 8 a 12 ya terminó, que es como lo entiende la gente.
        var actual = deHoy.FirstOrDefault(h => hora >= h.HoraInicio && hora < h.HoraFin);
        if (actual is not null)
            return new MedicoDisponibilidad(medico, true, actual,
                $"Atiende hasta las {Formatear(actual.HoraFin)}", dias);

        // No está atendiendo: ¿le queda algún tramo más hoy?
        var proximo = deHoy.FirstOrDefault(h => h.HoraInicio > hora);
        if (proximo is not null)
            return new MedicoDisponibilidad(medico, false, proximo,
                $"Entra a las {Formatear(proximo.HoraInicio)}", dias);

        return new MedicoDisponibilidad(medico, false, null, "Ya terminó por hoy", dias);
    }

    /// <summary>
    /// Los días en que el médico tiene al menos un tramo, ordenados de lunes a
    /// domingo (y no de domingo a sábado como los numera MySQL): la semana
    /// laboral se lee empezando por el lunes.
    /// </summary>
    public static IReadOnlyList<int> DiasQueAtiende(IEnumerable<MedicoHorario> horarios) =>
        horarios.Select(h => h.DiaSemana)
                .Distinct()
                .OrderBy(OrdenLaboral)
                .ToList();

    /// <summary>Lunes primero, domingo último. Domingo es 1 en MySQL, así que va al final.</summary>
    private static int OrdenLaboral(int diaSemana) => diaSemana == 1 ? 8 : diaSemana;

    /// <summary>
    /// Día de la semana con la MISMA convención que MySQL DAYOFWEEK():
    /// 1 = domingo … 7 = sábado. En .NET, DayOfWeek.Sunday vale 0, así que
    /// hay que sumar uno. Mezclar las dos convenciones haría que los horarios
    /// aparecieran corridos un día, y eso se ve tarde.
    /// </summary>
    public static int DiaSemanaDe(DateTime momento) => (int)momento.DayOfWeek + 1;

    /// <summary>Nombre del día para la UI, en español dominicano.</summary>
    public static string NombreDia(int diaSemana) => diaSemana switch
    {
        1 => "Domingo",
        2 => "Lunes",
        3 => "Martes",
        4 => "Miércoles",
        5 => "Jueves",
        6 => "Viernes",
        7 => "Sábado",
        _ => throw new ArgumentOutOfRangeException(nameof(diaSemana), diaSemana,
                 "El día de la semana va de 1 (domingo) a 7 (sábado).")
    };

    /// <summary>Nombre corto para las columnas angostas y las tarjetas: "Lun", "Mié".</summary>
    public static string NombreDiaCorto(int diaSemana) => diaSemana switch
    {
        1 => "Dom",
        2 => "Lun",
        3 => "Mar",
        4 => "Mié",
        5 => "Jue",
        6 => "Vie",
        7 => "Sáb",
        _ => throw new ArgumentOutOfRangeException(nameof(diaSemana), diaSemana,
                 "El día de la semana va de 1 (domingo) a 7 (sábado).")
    };

    /// <summary>
    /// Los días de atención en una sola línea: "Lun, Mar, Vie". Con los siete
    /// dice "Todos los días" y sin ninguno "Sin días fijos" — leer siete
    /// abreviaturas seguidas cuesta más que leer dos palabras.
    /// </summary>
    public static string ResumirDias(IReadOnlyList<int> dias)
    {
        if (dias.Count == 0) return "Sin días fijos";
        if (dias.Count == 7) return "Todos los días";
        return string.Join(", ", dias.Select(NombreDiaCorto));
    }

    private static string Formatear(TimeOnly hora) => hora.ToString("h:mm tt", CulturaRd);

    /// <summary>
    /// Valida un tramo antes de guardarlo. Además de que la hora de fin sea
    /// posterior a la de inicio, comprueba que no se PISE con otro tramo del
    /// mismo día: dos tramos superpuestos harían que el médico apareciera
    /// disponible en dos lugares a la vez y que la agenda ofreciera el mismo
    /// hueco dos veces.
    /// </summary>
    public static void ValidarTramo(HorarioDatos nuevo, IEnumerable<MedicoHorario> existentes,
        long? exceptoId = null)
    {
        if (nuevo.DiaSemana is < 1 or > 7)
            throw new ArgumentException("El día de la semana va de 1 (domingo) a 7 (sábado).");
        if (nuevo.HoraFin <= nuevo.HoraInicio)
            throw new ArgumentException("La hora de salida tiene que ser posterior a la de entrada.");

        var choque = existentes.FirstOrDefault(h =>
            h.Id != exceptoId &&
            h.DiaSemana == nuevo.DiaSemana &&
            nuevo.HoraInicio < h.HoraFin &&
            h.HoraInicio < nuevo.HoraFin);

        if (choque is not null)
            throw new ArgumentException(
                $"Ese horario se pisa con el de {Formatear(choque.HoraInicio)} a " +
                $"{Formatear(choque.HoraFin)} del mismo día.");
    }
}
