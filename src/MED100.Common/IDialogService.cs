namespace MED100.Common;

/// <summary>
/// Diálogos de la aplicación, inyectable para poder testear ViewModels
/// sin UI real (regla del proyecto: nunca MessageBox.Show directo en lógica).
/// </summary>
public interface IDialogService
{
    /// <summary>Pregunta Sí/No. True si el usuario confirma.</summary>
    bool Confirmar(string titulo, string mensaje);

    void Informar(string titulo, string mensaje);

    void MostrarError(string titulo, string mensaje);

    /// <summary>
    /// Pide un texto al usuario (ej: motivo de anulación).
    /// Devuelve null si cancela.
    /// </summary>
    string? PedirTexto(string titulo, string mensaje, string textoInicial = "");
}
