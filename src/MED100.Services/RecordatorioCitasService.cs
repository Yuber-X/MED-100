using System.Globalization;
using System.Text;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>Resultado de una tanda de recordatorios de cita.</summary>
public record ResultadoRecordatorioCitas(int Enviados, int Fallidos, int SinCorreo, string Detalle)
{
    public int Total => Enviados + Fallidos;
}

/// <summary>
/// Recordatorio de cita por correo al PACIENTE ("colocar cita recordatorio por
/// correo", pedido del cliente 2026-08-08).
///
/// Es otra cosa que <see cref="RecordatorioCaducidadService"/>: aquel manda UN
/// correo al dueño; este manda UNO POR PACIENTE. Por eso van separados y cada
/// uno tiene su interruptor.
///
/// <b>La regla que no se negocia:</b> <c>recordatorio_enviado_at</c> se marca
/// DESPUÉS de que el correo salió bien, y por cita. Marcarlo antes, o marcar
/// todas de una, haría que un fallo de red a mitad de la tanda dejara a unos
/// cuantos pacientes sin aviso y al sistema convencido de habérselos mandado —
/// y eso se descubre cuando no aparecen.
/// </summary>
public class RecordatorioCitasService
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly CitaRepository _citas;
    private readonly EmailService _email;
    private readonly AjustesLocales _ajustes;
    private readonly ConfiguracionNegocioService _negocio;

    public RecordatorioCitasService(CitaRepository citas, EmailService email,
        AjustesLocales ajustes, ConfiguracionNegocioService negocio)
    {
        _citas = citas;
        _email = email;
        _ajustes = ajustes;
        _negocio = negocio;
    }

    /// <summary>
    /// Manda los recordatorios de las citas que caen dentro de la ventana de
    /// anticipación y todavía no lo recibieron.
    /// </summary>
    public async Task<ResultadoRecordatorioCitas> EnviarAsync(CancellationToken ct = default)
    {
        if (!_email.EstaConfigurado)
            throw new InvalidOperationException(
                "El correo no está configurado. Completá la cuenta de Gmail en Configuración.");

        var horas = Math.Clamp(_ajustes.RecordatorioCitasHorasAntes, 1, 168);
        var ahoraUtc = DateTime.UtcNow;
        // Desde AHORA, no desde el principio del día: una cita que ya pasó no
        // necesita recordatorio, y mandarlo sería peor que no mandar nada.
        var hastaUtc = ahoraUtc.AddHours(horas);

        var pendientes = await _citas.ObtenerPendientesDeRecordatorioAsync(ahoraUtc, hastaUtc, ct);

        if (pendientes.Count == 0)
        {
            MarcarCorrida();
            return new ResultadoRecordatorioCitas(0, 0, 0,
                $"No hay citas sin avisar en las próximas {horas} horas.");
        }

        var enviados = 0;
        var fallidos = 0;

        foreach (var cita in pendientes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _email.EnviarAsync(cita.PacienteEmail!, Asunto(cita), Cuerpo(cita), ct);
                // Solo ahora. Ver el comentario de la clase.
                await _citas.MarcarRecordatorioEnviadoAsync(cita.Id, ct);
                enviados++;
            }
            catch (Exception ex)
            {
                // Que falle uno no puede cortar la tanda: los demás pacientes
                // no tienen la culpa de que un correo esté mal escrito.
                Log.Error(ex, "No se pudo enviar el recordatorio de la cita {CitaId}", cita.Id);
                fallidos++;
            }
        }

        MarcarCorrida();
        Log.Information("Recordatorios de cita: {Enviados} enviados, {Fallidos} fallidos", enviados, fallidos);

        var detalle = $"{enviados} recordatorio(s) enviado(s).";
        if (fallidos > 0)
            detalle += $" {fallidos} no se pudieron enviar (quedan pendientes para la próxima).";
        return new ResultadoRecordatorioCitas(enviados, fallidos, 0, detalle);
    }

    /// <summary>Envío automático al abrir el programa, una vez por día.</summary>
    public async Task EjecutarAutomaticoSiTocaAsync()
    {
        try
        {
            if (!_ajustes.RecordatorioCitasActivo || !_email.EstaConfigurado)
                return;
            if (_ajustes.UltimoRecordatorioCitasUtc is { } ultimo &&
                (DateTime.UtcNow - ultimo).TotalHours < 20)
                return;

            var r = await EnviarAsync();
            Log.Information("Recordatorios automáticos de cita: {Enviados} enviados, {Fallidos} fallidos",
                r.Enviados, r.Fallidos);
        }
        catch (Exception ex)
        {
            // Un fallo de correo NUNCA puede impedir abrir el programa ni atender.
            Log.Error(ex, "Falló el envío automático de recordatorios de cita");
        }
    }

    private void MarcarCorrida()
    {
        _ajustes.UltimoRecordatorioCitasUtc = DateTime.UtcNow;
        _ajustes.Guardar();
    }

    private string Asunto(Cita cita)
    {
        var local = FechaNegocio.AUtcLocal(cita.FechaHoraUtc);
        return $"Recordatorio de su cita — {local.ToString("dddd d 'de' MMMM", CulturaRd)} " +
               $"a las {local.ToString("h:mm tt", CulturaRd)}";
    }

    /// <summary>
    /// Texto plano y corto. Nada de diagnósticos ni de por qué viene: el correo
    /// puede terminar en cualquier bandeja y los datos de salud son sensibles
    /// (Ley 172-13). Solo cuándo, con quién y dónde.
    /// </summary>
    private string Cuerpo(Cita cita)
    {
        var local = FechaNegocio.AUtcLocal(cita.FechaHoraUtc);
        var negocio = _negocio.Actual;

        var sb = new StringBuilder();
        sb.AppendLine($"Hola {cita.PacienteNombre},");
        sb.AppendLine();
        sb.AppendLine("Le recordamos su cita:");
        sb.AppendLine();
        sb.AppendLine($"  Fecha:  {local.ToString("dddd d 'de' MMMM 'de' yyyy", CulturaRd)}");
        sb.AppendLine($"  Hora:   {local.ToString("h:mm tt", CulturaRd)}");
        sb.AppendLine($"  Médico: {cita.MedicoNombre}");
        if (!string.IsNullOrWhiteSpace(negocio.Direccion))
            sb.AppendLine($"  Lugar:  {negocio.Direccion}");
        sb.AppendLine();
        sb.AppendLine("Le agradecemos llegar unos minutos antes.");
        if (!string.IsNullOrWhiteSpace(negocio.Telefono))
            sb.AppendLine($"Si no puede asistir, avísenos al {negocio.Telefono}.");
        sb.AppendLine();
        sb.AppendLine(negocio.NombreNegocio);
        return sb.ToString();
    }
}
