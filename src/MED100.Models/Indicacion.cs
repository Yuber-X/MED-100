namespace MED100.Models;

/// <summary>
/// Un medicamento indicado. Los campos van sueltos y no en un texto libre
/// porque es lo que permite releer el día ordenado, que es exactamente lo que
/// se pidió (2026-09-06).
///
/// Solo <see cref="Medicamento"/> es obligatorio: en la vida real el médico
/// dice "amoxicilina 500 cada 8 por 7 días", pero también dice "algo para el
/// dolor". Exigir dosis y frecuencia haría que se anote mal con tal de guardar.
/// </summary>
public class IndicacionMedicamento
{
    public long Id { get; set; }
    public long IndicacionId { get; set; }
    public string Medicamento { get; set; } = string.Empty;
    public string? Dosis { get; set; }
    public string? Frecuencia { get; set; }
    public string? Duracion { get; set; }
    public string? Instrucciones { get; set; }

    /// <summary>Todo en un renglón, como se lee en la lista y en el historial.</summary>
    public string Resumen
    {
        get
        {
            var partes = new List<string> { Medicamento };
            if (!string.IsNullOrWhiteSpace(Dosis)) partes.Add(Dosis!);
            if (!string.IsNullOrWhiteSpace(Frecuencia)) partes.Add(Frecuencia!);
            if (!string.IsNullOrWhiteSpace(Duracion)) partes.Add(Duracion!);
            var texto = string.Join(" · ", partes);
            return string.IsNullOrWhiteSpace(Instrucciones)
                ? texto
                : $"{texto} ({Instrucciones})";
        }
    }
}

/// <summary>
/// Lo que el médico le indicó a un paciente en una visita (013).
///
/// ⚠ Es contenido clínico y por eso vive acotado: se anota QUÉ se mandó a
/// tomar, nunca POR QUÉ. No hay diagnóstico, ni evolución, ni antecedentes.
/// Ver CLAUDE.md §1.1 antes de agregarle un campo.
/// </summary>
public class Indicacion
{
    public long Id { get; set; }
    public long ClienteId { get; set; }
    public long? MedicoId { get; set; }
    public long? CitaId { get; set; }
    public DateTime FechaUtc { get; set; }
    /// <summary>Quién la escribió en el sistema (no necesariamente el médico).</summary>
    public long UsuarioId { get; set; }
    public string? Notas { get; set; }

    public List<IndicacionMedicamento> Medicamentos { get; set; } = [];

    // ---- Solo para mostrar; no se persisten acá ----
    public string ClienteNombre { get; set; } = string.Empty;
    public string? MedicoNombre { get; set; }
    public string? UsuarioNombre { get; set; }

    public string ResumenMedicamentos =>
        Medicamentos.Count == 0
            ? "—"
            : string.Join("  ·  ", Medicamentos.Select(m => m.Medicamento));
}
