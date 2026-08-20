using System.Globalization;
using System.Text;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>Resultado de una tanda de avisos de caducidad.</summary>
public record ResultadoAvisoCaducidad(
    int Caducados,
    int PorCaducar,
    bool CorreoEnviado,
    string Detalle)
{
    public int Total => Caducados + PorCaducar;
}

/// <summary>
/// Aviso por correo de productos próximos a caducar (pedido del cliente
/// 2026-07-30):
///
///   "Recordatorios por correo (gmail) habrá que modificarlo, en vez de enviar
///    un correo por cliente y al dueño, que sea solo al dueño que avise de los
///    productos vencidos."
///
/// Es el reemplazo del RecordatorioService en el punto de venta. Tiene sentido:
/// en préstamos el correo sirve para EMPUJAR al cliente a pagar; en el punto de
/// venta el cliente no debe nada, y lo que corre riesgo es la mercancía. El
/// destinatario es UNO SOLO: el dueño.
///
/// La ventana de días es la MISMA que usa el aviso al iniciar
/// (AvisoCaducidadDias). Dos perillas para la misma idea se terminan
/// contradiciendo, y el dueño no sabría cuál manda.
///
/// NO manda correo si no hay nada que avisar: un correo diario diciendo "todo
/// bien" se convierte en ruido y se deja de leer, que es justo lo contrario de
/// lo que se busca.
/// </summary>
public class RecordatorioCaducidadService
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly ProductoRepository _productos;
    private readonly EmailService _email;
    private readonly AjustesLocales _ajustes;
    /// <summary>El nombre del negocio firma el correo; vive en la BD, no en ajustes.json.</summary>
    private readonly ConfiguracionNegocioService _negocio;

    public RecordatorioCaducidadService(ProductoRepository productos, EmailService email,
        AjustesLocales ajustes, ConfiguracionNegocioService negocio)
    {
        _productos = productos;
        _email = email;
        _ajustes = ajustes;
        _negocio = negocio;
    }

    /// <summary>
    /// Manda el resumen al dueño. Devuelve qué se hizo, para mostrarlo en
    /// pantalla cuando el envío es a pedido.
    /// </summary>
    public async Task<ResultadoAvisoCaducidad> EnviarAsync(CancellationToken ct = default)
    {
        if (!_email.EstaConfigurado)
            throw new InvalidOperationException(
                "El correo no está configurado. Completá la cuenta de Gmail en Configuración.");
        if (string.IsNullOrWhiteSpace(_ajustes.CorreoDueno))
            throw new InvalidOperationException(
                "Falta el correo del dueño: es el único destinatario de este aviso. " +
                "Cargalo en Configuración → Recordatorios por correo.");

        var hoy = FechaNegocio.Hoy;
        var limite = hoy.AddDays(Math.Max(1, _ajustes.AvisoCaducidadDias));

        // Solo lo que tiene existencia: avisar por un producto con 0 unidades
        // es hacerle perder el tiempo al dueño, no hay nada que rematar ni sacar.
        var enRiesgo = (await _productos.ObtenerConCaducidadAsync(ct))
            .Where(p => p.Cantidad > 0 && p.FechaCaducidad is { } f && f <= limite)
            .OrderBy(p => p.FechaCaducidad)
            .ToList();

        var caducados = enRiesgo.Count(p => p.FechaCaducidad!.Value < hoy);
        var porCaducar = enRiesgo.Count - caducados;

        if (enRiesgo.Count == 0)
        {
            _ajustes.UltimoRecordatorioUtc = DateTime.UtcNow;
            _ajustes.Guardar();
            return new ResultadoAvisoCaducidad(0, 0, false,
                $"No hay productos caducados ni por caducar en los próximos " +
                $"{_ajustes.AvisoCaducidadDias} días. No se envió correo.");
        }

        try
        {
            await _email.EnviarAsync(_ajustes.CorreoDueno, Asunto(caducados, porCaducar),
                Cuerpo(enRiesgo, hoy, caducados), ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo enviar el aviso de caducidad al dueño");
            return new ResultadoAvisoCaducidad(caducados, porCaducar, false,
                $"No se pudo enviar el correo: {ex.Message}");
        }

        _ajustes.UltimoRecordatorioUtc = DateTime.UtcNow;
        _ajustes.Guardar();

        Log.Information("Aviso de caducidad enviado: {Caducados} caducados, {PorCaducar} por caducar",
            caducados, porCaducar);
        return new ResultadoAvisoCaducidad(caducados, porCaducar, true,
            $"Resumen enviado a {_ajustes.CorreoDueno}.");
    }

    /// <summary>Envío automático al entrar al punto de venta (una vez por día).</summary>
    public async Task EjecutarAutomaticoSiTocaAsync()
    {
        try
        {
            if (!_ajustes.RecordatoriosAutomaticos || !_email.EstaConfigurado)
                return;
            if (string.IsNullOrWhiteSpace(_ajustes.CorreoDueno))
                return;
            // Una vez por día de negocio
            if (_ajustes.UltimoRecordatorioUtc is { } ultimo &&
                (DateTime.UtcNow - ultimo).TotalHours < 20)
                return;

            var r = await EnviarAsync();
            Log.Information("Aviso automático de caducidad: {Caducados} caducados, {PorCaducar} por caducar, enviado={Enviado}",
                r.Caducados, r.PorCaducar, r.CorreoEnviado);
        }
        catch (Exception ex)
        {
            // Un fallo de correo NUNCA puede impedir vender
            Log.Error(ex, "Falló el aviso automático de caducidad");
        }
    }

    private static string Asunto(int caducados, int porCaducar) => caducados > 0
        ? $"URGENTE: {caducados} producto(s) CADUCADO(S) en el inventario"
        : $"{porCaducar} producto(s) por caducar";

    private string Cuerpo(IReadOnlyList<Producto> productos, DateOnly hoy, int caducados)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Estado de la mercancía al {hoy.ToString(@"dd'/'MM'/'yyyy", CulturaRd)}:");
        sb.AppendLine();

        if (caducados > 0)
        {
            sb.AppendLine($"=== YA CADUCADOS ({caducados}) — sacar de la venta ===");
            foreach (var p in productos.Where(p => p.FechaCaducidad!.Value < hoy))
                sb.AppendLine(Linea(p, hoy));
            sb.AppendLine();
        }

        var porVencer = productos.Where(p => p.FechaCaducidad!.Value >= hoy).ToList();
        if (porVencer.Count > 0)
        {
            sb.AppendLine($"=== POR CADUCAR en los próximos {_ajustes.AvisoCaducidadDias} días ({porVencer.Count}) ===");
            foreach (var p in porVencer)
                sb.AppendLine(Linea(p, hoy));
            sb.AppendLine();
        }

        // El valor en riesgo es lo que decide si vale la pena rematar: sin el
        // número, el dueño tiene que sacar la cuenta a mano.
        var valor = productos.Sum(p => p.Precio * p.Cantidad);
        sb.AppendLine($"Valor en riesgo: RD$ {valor.ToString("N2", CulturaRd)} " +
                      $"({productos.Sum(p => p.Cantidad)} unidades).");
        sb.AppendLine();
        sb.AppendLine(_negocio.Actual.NombreNegocio);
        return sb.ToString();
    }

    private static string Linea(Producto p, DateOnly hoy)
    {
        var dias = p.FechaCaducidad!.Value.DayNumber - hoy.DayNumber;
        var cuando = dias < 0 ? $"caducó hace {-dias} día(s)"
            : dias == 0 ? "caduca HOY"
            : $"caduca en {dias} día(s)";
        var codigo = string.IsNullOrWhiteSpace(p.Codigo) ? "" : $"[{p.Codigo}] ";
        return $"• {codigo}{p.Nombre} — {p.Cantidad} u. — " +
               $"{p.FechaCaducidad.Value.ToString(@"dd'/'MM'/'yyyy", CulturaRd)} ({cuando}) — " +
               $"RD$ {(p.Precio * p.Cantidad).ToString("N2", CulturaRd)}";
    }
}
