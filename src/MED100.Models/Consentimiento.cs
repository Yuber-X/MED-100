namespace MED100.Models;

/// <summary>
/// Una PLANTILLA de consentimiento informado (015). Pedido de la clínica del
/// 2026-09-21: un botón que liste los documentos de consentimiento por
/// procedimiento, para firmar antes de cada uno.
///
/// ⚠ Esto es el TEXTO EN BLANCO, no el consentimiento firmado. El firmado es el
/// papel que el paciente firma a mano y que se escanea al expediente
/// (decisión de Yuber, 2026-09-24: imprimir y escanear). Que exista una
/// plantilla no significa que alguien la haya firmado.
/// </summary>
public class Consentimiento
{
    public long Id { get; set; }

    /// <summary>
    /// A qué procedimiento pertenece. NULL = general: sirve para cualquiera. Es
    /// el caso de la clínica que tiene UN papel para todo.
    /// </summary>
    public long? ProcedimientoId { get; set; }

    /// <summary>Nombre del procedimiento, resuelto por JOIN. No se persiste acá.</summary>
    public string? ProcedimientoNombre { get; set; }

    public string Titulo { get; set; } = string.Empty;
    public string Cuerpo { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Para qué sirve, tal como se lee en la lista.</summary>
    public string Alcance => ProcedimientoNombre ?? "General · cualquier procedimiento";

    /// <summary>Primeras palabras del texto, para reconocerlo sin abrirlo.</summary>
    public string Asomo
    {
        get
        {
            var plano = Cuerpo.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return plano.Length <= 120 ? plano : plano[..120] + "…";
        }
    }
}

/// <summary>La plantilla tal como la captura el formulario.</summary>
public record ConsentimientoDatos(long? ProcedimientoId, string Titulo, string Cuerpo,
    bool Activo = true);

/// <summary>
/// El consentimiento ya combinado con el paciente, listo para imprimir.
///
/// Se arma una vez al imprimir en lugar de que el documento salga a buscar los
/// datos: lo que sale por la impresora es exactamente lo que se le pasó, y se
/// puede revisar sin una impresora enfrente.
/// </summary>
public record ConsentimientoImpreso(
    string Titulo,
    string Cuerpo,
    string? ProcedimientoNombre,
    string PacienteNombre,
    string? PacienteCedula,
    string? MedicoNombre,
    DateTime FechaUtc);
