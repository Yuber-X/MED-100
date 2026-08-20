using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MED100.Common;

/// <summary>Tamaño de texto de la interfaz (patrón PrestControl).</summary>
public enum TamanoTexto
{
    Pequeno,
    Mediano,
    Grande
}

/// <summary>
/// Preferencias locales del equipo (NO van a la base de datos: son por PC).
/// Se persisten como JSON junto al ejecutable. Los datos compartidos del
/// negocio (ITBIS, RNC, numeración...) viven en la tabla configuracion_negocio.
/// </summary>
public class AjustesLocales
{
    public TamanoTexto TamanoTexto { get; set; } = TamanoTexto.Pequeno;

    // Export automático a Excel (activable en Configuración)
    public bool ExportAutomaticoActivo { get; set; }
    public int ExportAutomaticoCadaDias { get; set; } = 30;
    public string? ExportAutomaticoCarpeta { get; set; }
    public DateTime? UltimaExportacionUtc { get; set; }

    // Aviso al iniciar sesión (spec §8.2): caducidad + stock bajo
    public bool AvisoCaducidadActivo { get; set; } = true;
    public int AvisoCaducidadDias { get; set; } = 30;
    public bool AvisoStockBajoActivo { get; set; } = true;
    public int AvisoStockBajoUmbral { get; set; } = 10;
    /// <summary>Ids de productos silenciados con "No volver a avisar".</summary>
    public List<long> AvisoProductosSilenciados { get; set; } = [];

    // Impresión y ticket (spec §8.3.G — preferencia por terminal)
    public string? ImpresoraPredeterminada { get; set; }
    public string TamanoPapel { get; set; } = "80mm";      // 80mm | Carta
    public int CopiasTicket { get; set; } = 1;
    public string? TicketEncabezado { get; set; }
    /// <summary>OFF (default): al cobrar se imprime directo sin preguntar (pedido Yuber 2026-07-12).</summary>
    public bool MostrarVistaPreviaTicket { get; set; }
    public string? TicketPie { get; set; } = "Gracias por su compra";

    // Gestión de sesión (spec §8.3.J)
    public int MinutosInactividadLogout { get; set; } = 30;   // 0 = nunca
    public bool RequierePasswordOperacionesSensibles { get; set; } = true;

    /// <summary>
    /// Dónde viven los archivos del expediente de los pacientes. Vacío = junto
    /// al ejecutable, en <c>expedientes\</c>.
    ///
    /// Es configurable porque escanear cédulas y estudios llena un disco: puede
    /// apuntar a otra unidad. Es preferencia POR PC, no del negocio — cada
    /// terminal puede tener el disco en otro lado.
    /// </summary>
    public string? CarpetaExpedientes { get; set; }

    // Cierre automático del cuadre de caja (pedido Yuber 2026-07-12).
    // A la hora indicada, la app cierra el cuadre del día de todos los cajeros
    // con ventas. Es por equipo: se activa en la PC que queda encendida.
    public bool CierreAutomaticoActivo { get; set; }
    /// <summary>Hora del día de negocio (0–23) en que se cierra la caja.</summary>
    public int CierreAutomaticoHora { get; set; } = 22;
    /// <summary>Último día de negocio ya cerrado automáticamente (evita repetir).</summary>
    public DateOnly? UltimoCierreAutomatico { get; set; }

    private static readonly string Ruta = Path.Combine(AppContext.BaseDirectory, "ajustes.json");
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    /// <summary>Factor de escala de la UI según el tamaño elegido.</summary>
    public double FactorEscala => TamanoTexto switch
    {
        TamanoTexto.Mediano => 1.12,
        TamanoTexto.Grande => 1.25,
        _ => 1.0
    };

    public static AjustesLocales Cargar()
    {
        try
        {
            if (File.Exists(Ruta))
                return JsonSerializer.Deserialize<AjustesLocales>(File.ReadAllText(Ruta)) ?? new AjustesLocales();
        }
        catch (Exception)
        {
            // Archivo corrupto → se regenera con defaults (no es dato crítico)
        }
        return new AjustesLocales();
    }

    public void Guardar() => File.WriteAllText(Ruta, JsonSerializer.Serialize(this, Opciones));

    // ---------- Aviso de caducidad por correo (Gmail) ----------
    // Portado de la suite el 2026-07-31. En el punto de venta el correo NO va a
    // los clientes: va al DUEÑO, avisandole de la mercancia que se esta por
    // vencer. El cliente del mostrador no debe nada; lo que corre riesgo es el
    // inventario.

    public bool RecordatoriosActivos { get; set; }
    /// <summary>Cuenta Gmail que ENVIA el aviso.</summary>
    public string GmailRemitente { get; set; } = string.Empty;
    /// <summary>
    /// Contrasena de APLICACION de Gmail, cifrada con DPAPI (nunca texto
    /// plano). Se lee y escribe con GmailAppPassword; este campo es el blob
    /// que se persiste en el JSON.
    /// </summary>
    public string GmailAppPasswordCifrada { get; set; } = string.Empty;
    /// <summary>Correo del dueno: el UNICO destinatario del aviso.</summary>
    public string CorreoDueno { get; set; } = string.Empty;
    /// <summary>Enviar el aviso automaticamente al abrir el programa.</summary>
    public bool RecordatoriosAutomaticos { get; set; }
    public DateTime? UltimoRecordatorioUtc { get; set; }

    // ---------- Recordatorio de citas al PACIENTE ----------
    // Es otra cosa que el aviso de caducidad: aquel va al dueno y este va a
    // cada paciente. Por eso tiene su propio interruptor y su propia marca de
    // ultima corrida.

    /// <summary>Mandar el recordatorio de cita por correo al paciente.</summary>
    public bool RecordatorioCitasActivo { get; set; } = true;

    /// <summary>
    /// Con cuanta anticipacion se avisa. 24h por defecto: el dia antes, que es
    /// cuando el paciente todavia puede reacomodarse o avisar que no viene.
    /// </summary>
    public int RecordatorioCitasHorasAntes { get; set; } = 24;

    public DateTime? UltimoRecordatorioCitasUtc { get; set; }

    /// <summary>Contrasena de app de Gmail en texto plano (cifra/descifra con DPAPI).</summary>
    [JsonIgnore]
    public string GmailAppPassword
    {
        get => Secreto.Revelar(GmailAppPasswordCifrada);
        set => GmailAppPasswordCifrada = Secreto.Proteger(value);
    }
}
