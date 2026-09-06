using System.Globalization;
using System.Text;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>Resultado de una tanda de avisos.</summary>
public record ResultadoAvisoCaducidad(
    int Caducados,
    int PorCaducar,
    bool CorreoEnviado,
    string Detalle,
    /// <summary>Deudas de pacientes atrasadas o por vencer (012).</summary>
    int FiadosEnRiesgo = 0)
{
    public int Total => Caducados + PorCaducar + FiadosEnRiesgo;
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
///
/// <b>DESDE LA 012 TAMBIÉN AVISA DE LOS FIADOS</b> (pedido de Yuber, 2026-09-06:
/// <i>"parecido a los préstamos a punto de caducar, pero orientado a los fiados
/// a clientes… habrá que agregarlos a notificaciones automáticas"</i>).
///
/// Van en el MISMO correo y no en uno aparte, y esa es la decisión importante:
/// dos correos diarios del mismo sistema se dejan de leer los dos. El nombre de
/// la clase quedó de cuando solo miraba la mercancía; hoy es el aviso diario del
/// negocio.
/// </summary>
public class RecordatorioCaducidadService
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly ProductoRepository _productos;
    private readonly FiadoRepository _fiados;
    private readonly EmailService _email;
    private readonly AjustesLocales _ajustes;
    /// <summary>El nombre del negocio firma el correo; vive en la BD, no en ajustes.json.</summary>
    private readonly ConfiguracionNegocioService _negocio;

    public RecordatorioCaducidadService(ProductoRepository productos, FiadoRepository fiados,
        EmailService email, AjustesLocales ajustes, ConfiguracionNegocioService negocio)
    {
        _productos = productos;
        _fiados = fiados;
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

        // Deudas atrasadas o que vencen dentro de la ventana configurada. Las
        // que no tienen fecha acordada quedan FUERA: no están atrasadas, y
        // meterlas todos los días convertiría el aviso en una lista fija que
        // nadie mira.
        var fiados = await ObtenerFiadosEnRiesgoAsync(hoy, ct);

        if (enRiesgo.Count == 0 && fiados.Count == 0)
        {
            _ajustes.UltimoRecordatorioUtc = DateTime.UtcNow;
            _ajustes.Guardar();
            return new ResultadoAvisoCaducidad(0, 0, false,
                "No hay productos por caducar ni deudas por vencer. No se envió correo.");
        }

        try
        {
            await _email.EnviarAsync(_ajustes.CorreoDueno,
                Asunto(caducados, porCaducar, fiados.Count),
                Cuerpo(enRiesgo, hoy, caducados, fiados), ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo enviar el aviso diario al dueño");
            return new ResultadoAvisoCaducidad(caducados, porCaducar, false,
                $"No se pudo enviar el correo: {ex.Message}", fiados.Count);
        }

        _ajustes.UltimoRecordatorioUtc = DateTime.UtcNow;
        _ajustes.Guardar();

        Log.Information("Aviso diario enviado: {Caducados} caducados, {PorCaducar} por caducar, {Fiados} deudas",
            caducados, porCaducar, fiados.Count);
        return new ResultadoAvisoCaducidad(caducados, porCaducar, true,
            $"Resumen enviado a {_ajustes.CorreoDueno}.", fiados.Count);
    }

    /// <summary>
    /// Deudas que hay que reclamar: ya atrasadas, o que vencen dentro de la
    /// ventana configurada. Un fallo leyéndolas NO tumba el aviso de caducidad
    /// —son dos avisos independientes que comparten el sobre—, así que se
    /// registra y se sigue con la lista vacía.
    /// </summary>
    private async Task<IReadOnlyList<FiadoResumen>> ObtenerFiadosEnRiesgoAsync(
        DateOnly hoy, CancellationToken ct)
    {
        if (!_ajustes.AvisoFiadosActivo)
            return [];

        try
        {
            var limite = hoy.AddDays(Math.Max(0, _ajustes.AvisoFiadosDias));
            return (await _fiados.ObtenerPendientesAsync(ct))
                .Where(f => f.FechaCompromiso is { } fecha && fecha <= limite)
                .OrderBy(f => f.FechaCompromiso)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudieron leer los fiados para el aviso diario");
            return [];
        }
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

    /// <summary>
    /// El asunto nombra lo MÁS urgente que haya. Un asunto que enumera todo se
    /// vuelve ilegible en el teléfono, que es donde se lee.
    /// </summary>
    private static string Asunto(int caducados, int porCaducar, int fiados)
    {
        if (caducados > 0)
            return $"URGENTE: {caducados} producto(s) CADUCADO(S) en el inventario";
        if (porCaducar > 0 && fiados > 0)
            return $"{porCaducar} producto(s) por caducar y {fiados} deuda(s) por cobrar";
        if (porCaducar > 0)
            return $"{porCaducar} producto(s) por caducar";
        return $"{fiados} deuda(s) de pacientes por cobrar";
    }

    private string Cuerpo(IReadOnlyList<Producto> productos, DateOnly hoy, int caducados,
        IReadOnlyList<FiadoResumen> fiados)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Estado de la clínica al {hoy.ToString(@"dd'/'MM'/'yyyy", CulturaRd)}:");
        sb.AppendLine();

        if (fiados.Count > 0)
        {
            // Las deudas van PRIMERO: es plata de la clínica que está afuera, y
            // a diferencia de un insumo por vencer, se recupera llamando hoy.
            var atrasadas = fiados.Where(f => f.FechaCompromiso!.Value < hoy).ToList();
            var deudasPorVencer = fiados.Where(f => f.FechaCompromiso!.Value >= hoy).ToList();

            if (atrasadas.Count > 0)
            {
                sb.AppendLine($"=== DEUDAS ATRASADAS ({atrasadas.Count}) — llamar ===");
                foreach (var f in atrasadas)
                    sb.AppendLine(LineaFiado(f, hoy));
                sb.AppendLine();
            }

            if (deudasPorVencer.Count > 0)
            {
                sb.AppendLine($"=== DEUDAS POR VENCER en {_ajustes.AvisoFiadosDias} días ({deudasPorVencer.Count}) ===");
                foreach (var f in deudasPorVencer)
                    sb.AppendLine(LineaFiado(f, hoy));
                sb.AppendLine();
            }

            sb.AppendLine($"Total por cobrar: RD$ {fiados.Sum(f => f.Saldo).ToString("N2", CulturaRd)}.");
            sb.AppendLine();
        }

        if (productos.Count == 0)
        {
            sb.AppendLine(_negocio.Actual.NombreNegocio);
            return sb.ToString();
        }

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

    /// <summary>
    /// Una deuda en el correo. Lleva el teléfono adelante porque la acción que
    /// se espera es llamar, y buscarlo en el sistema es justo la fricción que
    /// hace que no se llame.
    /// </summary>
    private static string LineaFiado(FiadoResumen f, DateOnly hoy)
    {
        var tel = string.IsNullOrWhiteSpace(f.ClienteTelefono)
            ? "sin teléfono"
            : f.ClienteTelefono;
        return $"• {f.ClienteNombre} ({tel}) — debe RD$ {f.Saldo.ToString("N2", CulturaRd)} " +
               $"de {f.NumeroFactura} — " +
               $"{CalculadoraFiado.DescribirVencimiento(f.FechaCompromiso, hoy)}";
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
