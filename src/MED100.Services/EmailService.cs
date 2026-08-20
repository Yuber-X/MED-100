using System.Net;
using System.Net.Mail;
using MED100.Common;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Envío de correos por Gmail (SMTP con STARTTLS). Usa la cuenta y la
/// CONTRASEÑA DE APLICACIÓN configuradas (no la contraseña normal de Gmail:
/// Google exige App Password cuando hay verificación en 2 pasos).
///
/// La contraseña de app se guarda cifrada con DPAPI (ver Secreto). Este
/// servicio la recibe ya descifrada de AjustesLocales.
/// </summary>
public class EmailService
{
    private const string Host = "smtp.gmail.com";
    private const int Puerto = 587;   // STARTTLS

    private readonly AjustesLocales _ajustes;
    /// <summary>
    /// El nombre que ve el destinatario como remitente. En MED-100 vive en la
    /// BD (configuracion_negocio) y no en ajustes.json: es del NEGOCIO, no de
    /// esta terminal. En la suite estaba en los ajustes locales; acá el diseño
    /// original era mejor y se respeta.
    /// </summary>
    private readonly ConfiguracionNegocioService _negocio;

    public EmailService(AjustesLocales ajustes, ConfiguracionNegocioService negocio)
    {
        _ajustes = ajustes;
        _negocio = negocio;
    }

    /// <summary>True si hay cuenta y contraseña configuradas.</summary>
    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(_ajustes.GmailRemitente) &&
        !string.IsNullOrWhiteSpace(_ajustes.GmailAppPassword);

    /// <summary>
    /// Envía un correo. Lanza si SMTP falla (el llamador decide cómo avisar).
    ///
    /// Es `virtual` para que los tests puedan sustituirla: sin eso, probar qué
    /// productos entran en el aviso de caducidad o qué clientes reciben
    /// recordatorio exigiría abrir una conexión real a Gmail.
    /// </summary>
    public virtual async Task EnviarAsync(string destinatario, string asunto, string cuerpo,
        CancellationToken ct = default)
    {
        if (!EstaConfigurado)
            throw new InvalidOperationException(
                "El correo no está configurado. Completá la cuenta y la contraseña de aplicación en Configuración.");

        using var mensaje = new MailMessage
        {
            From = new MailAddress(_ajustes.GmailRemitente, _negocio.Actual.NombreNegocio),
            Subject = asunto,
            Body = cuerpo,
            IsBodyHtml = false
        };
        mensaje.To.Add(destinatario);

        using var cliente = new SmtpClient(Host, Puerto)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(_ajustes.GmailRemitente, _ajustes.GmailAppPassword)
        };

        await cliente.SendMailAsync(mensaje, ct);
        Log.Information("Correo enviado a {Destinatario}: {Asunto}", destinatario, asunto);
    }
}
