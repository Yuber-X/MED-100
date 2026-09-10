using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// Configuración — EXCLUSIVA del rol Admin (regla Yuber 2026-07-11).
///
/// Secciones:
///  · Apariencia            → tamaño de texto (escala TODA la UI, incluidos los
///                            encabezados de las tablas).
///  · Datos del negocio     → nombre y RNC (opcional); salen en el ticket.
///  · Cálculos e impuestos  → ITBIS activable + tasa + redondeo.
///  · Ventas y facturación  → mostrar/ocultar cliente en Vender.
///  · Impresión y ticket    → vista previa al cobrar (OFF = imprime directo).
///  · Cierre de caja        → cierre automático a una hora del día.
///  · Respaldo              → respaldar/restaurar la BD (migrar de equipo).
///  · Exportación a Excel   → manual y automática cada N días.
///
/// Dos capas: negocio → BD (configuracion_negocio); preferencias del equipo →
/// ajustes.json (AjustesLocales).
/// </summary>
public partial class ConfiguracionViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");

    private readonly ConfiguracionNegocioService _config;
    private readonly RespaldoService _respaldos;
    private readonly ExportacionService _exportacion;
    private readonly AjustesLocales _ajustes;
    private readonly IDialogService _dialogos;
    /// <summary>Aviso por correo de la mercancia proxima a caducar.</summary>
    private readonly RecordatorioCaducidadService _avisos;
    /// <summary>
    /// Recordatorio de cita al PACIENTE. Va aparte del de caducidad porque son
    /// dos cosas distintas: aquel manda UN correo al dueno y este manda uno por
    /// paciente. Comparten la cuenta de Gmail, nada mas.
    /// </summary>
    private readonly RecordatorioCitasService _recordatorioCitas;
    /// <summary>Catálogo de aseguradoras: se administra desde acá, no en el mostrador.</summary>
    private readonly ArsService _arsServicio;
    /// <summary>Demo de 15 días o versión completa.</summary>
    private readonly LicenciaService _licencias;
    /// <summary>Secuencia de comprobantes autorizada por la DGII (011).</summary>
    private readonly NcfService _ncf;

    /// <summary>El shell reescala la UI cuando cambia el tamaño de texto.</summary>
    public event Action<double>? EscalaCambiada;

    // ============================================================
    // Licencia (demo de 15 días / versión completa)
    // ============================================================
    // La tarjeta de acá solo INFORMA y abre la ventana de activación. El
    // formulario del código vive en un solo lugar (ActivacionViewModel), que
    // es el mismo que sale cuando se acaba la prueba: dos copias del mismo
    // campo se desincronizan al primer cambio de texto.

    /// <summary>El shell abre la ventana de activación.</summary>
    public event Action? ActivacionSolicitada;

    [ObservableProperty] private string _licenciaTitulo = string.Empty;
    [ObservableProperty] private string _licenciaDetalle = string.Empty;
    [ObservableProperty] private bool _licenciaActivada;

    private void RefrescarLicencia()
    {
        var estado = _licencias.Estado;
        LicenciaActivada = estado.Estado == EstadoLicencia.Completa;
        LicenciaTitulo = LicenciaActivada
            ? "Versión completa"
            : estado.Etiqueta;
        LicenciaDetalle = LicenciaActivada
            ? "Esta computadora está activada. No caduca."
            : $"Quedan {estado.DiasRestantes} días de prueba. Cuando se acaben, {AppInfo.Nombre} " +
              "pedirá la llave del producto para abrir; los datos siguen intactos.";
    }

    [RelayCommand]
    private void ActivarLicencia() => ActivacionSolicitada?.Invoke();

    public ConfiguracionViewModel(ConfiguracionNegocioService config, RespaldoService respaldos,
        ExportacionService exportacion, AjustesLocales ajustes, IDialogService dialogos,
        RecordatorioCaducidadService avisos, RecordatorioCitasService recordatorioCitas,
        ArsService arsServicio, LicenciaService licencias, NcfService ncf)
    {
        _config = config;
        _respaldos = respaldos;
        _exportacion = exportacion;
        _ajustes = ajustes;
        _dialogos = dialogos;
        _avisos = avisos;
        _recordatorioCitas = recordatorioCitas;
        _arsServicio = arsServicio;
        _licencias = licencias;
        _ncf = ncf;
        _licencias.Cambio += RefrescarLicencia;
        RefrescarLicencia();   // el evento solo avisa de los cambios: el estado inicial se lee acá

        // Correo: se leen los ajustes guardados. La contrasena NO se puede
        // rellenar en pantalla (la PasswordBox no lo permite por seguridad),
        // asi que este flag le avisa al usuario que YA hay una guardada.
        _recordatoriosActivos = ajustes.RecordatoriosActivos;
        _recordatoriosAutomaticos = ajustes.RecordatoriosAutomaticos;
        _gmailRemitente = ajustes.GmailRemitente;
        _correoDueno = ajustes.CorreoDueno;
        _avisoFiadosActivo = ajustes.AvisoFiadosActivo;
        _avisoFiadosDiasTexto = ajustes.AvisoFiadosDias.ToString(CulturaDo);
        _hayAppPasswordGuardada = !string.IsNullOrWhiteSpace(ajustes.GmailAppPasswordCifrada);
        _recordatorioCitasActivo = ajustes.RecordatorioCitasActivo;
        _recordatorioCitasHorasTexto = ajustes.RecordatorioCitasHorasAntes.ToString(CultureInfo.InvariantCulture);
        _avisoPacienteInactivoActivo = ajustes.AvisoPacienteInactivoActivo;
        _avisoPacienteInactivoMesesTexto = ajustes.AvisoPacienteInactivoMeses.ToString(CultureInfo.InvariantCulture);
        _ultimoAvisoTexto = ajustes.UltimoRecordatorioUtc is { } f
            ? $"Ultimo envio: {FechaNegocio.AUtcLocal(f):dd/MM/yyyy hh:mm tt}"
            : "Todavia no se envio ningun aviso.";

        Redondeos =
        [
            new Opcion<ModoRedondeo>(ModoRedondeo.Centavo, "Al centavo más cercano"),
            new Opcion<ModoRedondeo>(ModoRedondeo.Peso, "Al peso más cercano"),
            new Opcion<ModoRedondeo>(ModoRedondeo.Arriba, "Siempre hacia arriba")
        ];
        _redondeoSeleccionado = Redondeos[0];

        Tamanos =
        [
            new Opcion<TamanoTexto>(TamanoTexto.Pequeno, "Pequeño"),
            new Opcion<TamanoTexto>(TamanoTexto.Mediano, "Mediano"),
            new Opcion<TamanoTexto>(TamanoTexto.Grande, "Grande")
        ];
        _tamanoTextoSeleccionado = Tamanos[0];

        Horas = [.. Enumerable.Range(0, 24).Select(h =>
            new Opcion<int>(h, DateTime.Today.AddHours(h).ToString("hh:mm tt", CulturaDo)))];
        _horaCierreSeleccionada = Horas[22];
    }

    public IReadOnlyList<Opcion<ModoRedondeo>> Redondeos { get; }
    public IReadOnlyList<Opcion<TamanoTexto>> Tamanos { get; }
    public IReadOnlyList<Opcion<int>> Horas { get; }

    // --- Apariencia (ajustes.json) ---
    [ObservableProperty] private Opcion<TamanoTexto> _tamanoTextoSeleccionado;

    // --- Datos del negocio (BD) ---
    [ObservableProperty] private string _nombreNegocio = string.Empty;
    [ObservableProperty] private string _rnc = string.Empty;
    [ObservableProperty] private string _direccion = string.Empty;
    [ObservableProperty] private string _telefono = string.Empty;

    // --- Cálculos e impuestos (BD) ---
    [ObservableProperty] private bool _itbisActivo = true;
    [ObservableProperty] private string _itbisTasaTexto = "18";
    [ObservableProperty] private Opcion<ModoRedondeo> _redondeoSeleccionado;

    // --- Ventas (BD) ---
    [ObservableProperty] private bool _mostrarClienteEnVenta = true;

    // --- Impresión (ajustes.json) ---
    [ObservableProperty] private bool _mostrarVistaPreviaTicket;
    [ObservableProperty] private string _copiasTicketTexto = "1";
    [ObservableProperty] private string _ticketPie = string.Empty;

    // --- Cierre automático de caja (ajustes.json) ---
    [ObservableProperty] private bool _cierreAutomaticoActivo;
    [ObservableProperty] private Opcion<int> _horaCierreSeleccionada;

    // --- Exportación a Excel (ajustes.json) ---
    [ObservableProperty] private bool _exportAutomaticoActivo;
    [ObservableProperty] private string _exportCadaDiasTexto = "30";
    [ObservableProperty] private string _exportCarpeta = string.Empty;
    [ObservableProperty] private string _ultimaExportacionTexto = "Nunca";

    // ---------- Aseguradoras ----------
    public ObservableCollection<Ars> Aseguradoras { get; } = [];

    // ---------- Copias en el expediente (2026-08-15) ----------
    [ObservableProperty] private bool _archivarFacturaPdf = true;
    [ObservableProperty] private bool _archivarTurnoPdf;

    [ObservableProperty] private bool _arsActivo = true;
    [ObservableProperty] private string _arsNueva = string.Empty;
    [ObservableProperty] private Ars? _arsSeleccionada;
    [ObservableProperty] private string _mensajeArs = string.Empty;

    /// <summary>Agrega una ARS que no venía sembrada.</summary>
    [RelayCommand]
    private async Task AgregarArsAsync()
    {
        MensajeArs = string.Empty;
        if (string.IsNullOrWhiteSpace(ArsNueva))
        {
            MensajeArs = "Escribí el nombre de la aseguradora.";
            return;
        }
        try
        {
            await _arsServicio.CrearAsync(new ArsDatos(ArsNueva.Trim()));
            ArsNueva = string.Empty;
            await CargarArsAsync();
        }
        catch (Exception ex)
        {
            MensajeArs = ex.Message;
        }
    }

    /// <summary>
    /// Enciende o apaga una ARS. No se borra: una aseguradora con facturas
    /// tiene que seguir teniendo nombre cuando se reimprima el comprobante.
    /// </summary>
    [RelayCommand]
    private async Task AlternarArsAsync(Ars? ars)
    {
        if (ars is null)
            return;
        MensajeArs = string.Empty;
        try
        {
            await _arsServicio.ActualizarAsync(ars.Id,
                new ArsDatos(ars.Nombre, ars.Rnc, ars.Telefono, ars.Notas, !ars.Activo));
            await CargarArsAsync();
        }
        catch (Exception ex)
        {
            MensajeArs = ex.Message;
        }
    }

    private async Task CargarArsAsync()
    {
        try
        {
            var lista = await _arsServicio.ObtenerTodasAsync();
            Aseguradoras.Clear();
            foreach (var a in lista)
                Aseguradoras.Add(a);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando las aseguradoras");
        }
    }

    // ---------- Sala de espera ----------
    [ObservableProperty] private string _turnoPrefijo = string.Empty;
    [ObservableProperty] private bool _turnoImprimirAuto = true;

    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private string _mensajeExito = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public Task RefrescarAsync()
    {
        var cfg = _config.Actual;
        NombreNegocio = cfg.NombreNegocio;
        Rnc = cfg.Rnc ?? string.Empty;
        Direccion = cfg.Direccion ?? string.Empty;
        Telefono = cfg.Telefono ?? string.Empty;
        ItbisActivo = cfg.ItbisActivo;
        ItbisTasaTexto = cfg.ItbisTasa.ToString("0.##", CulturaDo);
        RedondeoSeleccionado = Redondeos.First(r => r.Valor == cfg.Redondeo);
        MostrarClienteEnVenta = cfg.MostrarClienteEnVenta;
        ArsActivo = cfg.ArsActivo;
        ArchivarFacturaPdf = cfg.ArchivarFacturaPdf;
        ArchivarTurnoPdf = cfg.ArchivarTurnoPdf;
        TurnoPrefijo = cfg.TurnoPrefijo;
        TurnoImprimirAuto = cfg.TurnoImprimirAuto;

        TamanoTextoSeleccionado = Tamanos.First(t => t.Valor == _ajustes.TamanoTexto);
        CarpetaExpedientes = ExpedienteService.CarpetaRaiz(_ajustes);
        MostrarVistaPreviaTicket = _ajustes.MostrarVistaPreviaTicket;
        CopiasTicketTexto = _ajustes.CopiasTicket.ToString(CulturaDo);
        TicketPie = _ajustes.TicketPie ?? string.Empty;

        CierreAutomaticoActivo = _ajustes.CierreAutomaticoActivo;
        HoraCierreSeleccionada = Horas[Math.Clamp(_ajustes.CierreAutomaticoHora, 0, 23)];

        ExportAutomaticoActivo = _ajustes.ExportAutomaticoActivo;
        ExportCadaDiasTexto = _ajustes.ExportAutomaticoCadaDias.ToString(CulturaDo);
        ExportCarpeta = _ajustes.ExportAutomaticoCarpeta ?? string.Empty;
        UltimaExportacionTexto = _ajustes.UltimaExportacionUtc is { } u
            ? FechaNegocio.AUtcLocal(u).ToString("dd/MM/yyyy hh:mm tt", CulturaDo)
            : "Nunca";

        MensajeError = MensajeExito = string.Empty;
        return Task.WhenAll(CargarArsAsync(), CargarNcfAsync());
    }

    // ============================================================
    // Comprobante fiscal (NCF) — 011, portado de FAControl
    // ============================================================
    // Vive en la BD (ncf_secuencia), no en ajustes.json: el rango que autorizó
    // la DGII es del NEGOCIO, no de esta computadora. Si mañana la clínica pone
    // una segunda terminal, las dos tienen que tomar números del mismo libro.

    [ObservableProperty] private bool _ncfActivo;
    [ObservableProperty] private string _ncfPrefijo = "B02";
    [ObservableProperty] private string _ncfLargoTexto = "8";
    [ObservableProperty] private string _ncfProximaTexto = "1";
    [ObservableProperty] private string _ncfFinTexto = string.Empty;
    [ObservableProperty] private DateTime? _ncfVencimiento;
    [ObservableProperty] private string _ncfEstadoTexto = string.Empty;

    /// <summary>
    /// True mientras <see cref="CargarNcfAsync"/> llena los campos desde la
    /// base. Sin esto, poner <see cref="NcfActivo"/> en true al cargar una
    /// secuencia guardada dispararía el borrado y la pantalla arrancaría vacía.
    /// </summary>
    private bool _cargandoNcf;

    public async Task CargarNcfAsync()
    {
        try
        {
            _cargandoNcf = true;
            var secuencia = await _ncf.ObtenerSecuenciaAsync();
            if (secuencia is null)
            {
                LimpiarNcf();
                return;
            }
            NcfActivo = secuencia.Activo;
            NcfPrefijo = secuencia.Prefijo;
            NcfLargoTexto = secuencia.Largo.ToString(CulturaDo);
            NcfProximaTexto = secuencia.Proxima.ToString(CulturaDo);
            NcfFinTexto = secuencia.FinRango?.ToString(CulturaDo) ?? string.Empty;
            NcfVencimiento = secuencia.Vencimiento?.ToDateTime(TimeOnly.MinValue);
            ActualizarEstadoNcf(secuencia);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la secuencia NCF");
            NcfEstadoTexto = "No se pudo cargar la configuración de comprobantes.";
        }
        finally
        {
            _cargandoNcf = false;
        }
    }

    /// <summary>
    /// Deja la sección en blanco cuando la clínica todavía NO cargó secuencia.
    /// Los valores que quedan son de fábrica, a modo de ejemplo (B02 =
    /// Consumidor Final, 8 dígitos, desde 1). No hay secuencia hasta que el
    /// Admin guarde: mientras tanto el NCF se sigue escribiendo a mano.
    /// </summary>
    private void LimpiarNcf()
    {
        NcfActivo = false;
        NcfPrefijo = "B02";
        NcfLargoTexto = "8";
        NcfProximaTexto = "1";
        NcfFinTexto = string.Empty;
        NcfVencimiento = null;
        NcfEstadoTexto =
            "La clínica todavía no cargó una secuencia: el NCF se escribe a mano en cada cobro. " +
            "Los valores de arriba son un ejemplo (B02 = Consumidor Final); al marcar la casilla " +
            "se borran para que escribas los que te autorizó la DGII.";
    }

    /// <summary>
    /// Al ENCENDER la casilla se vacían los campos, para que nadie active la
    /// numeración automática quedándose con los números de un talonario viejo
    /// sin darse cuenta. Un prefijo escrito nunca vuelve por accidente: o se
    /// escribe a mano, o entra por un comprobante realmente usado en un cobro.
    /// </summary>
    partial void OnNcfActivoChanged(bool value)
    {
        if (!value || _cargandoNcf)
            return;

        NcfPrefijo = string.Empty;
        NcfLargoTexto = string.Empty;
        NcfProximaTexto = string.Empty;
        NcfFinTexto = string.Empty;
        NcfVencimiento = null;

        NcfEstadoTexto =
            "Escribí la secuencia que le autorizó la DGII a la clínica: prefijo " +
            "(B02 tradicional, E32 e-CF), largo (8 o 10 dígitos), en qué número " +
            "arranca y hasta dónde llega el rango.";
    }

    private void ActualizarEstadoNcf(NcfSecuencia secuencia)
    {
        if (!secuencia.Activo)
        {
            NcfEstadoTexto = "Numeración automática apagada: el NCF se escribe a mano en cada cobro.";
            return;
        }

        var hoy = FechaNegocio.Hoy;
        if (secuencia.EstaVencida(hoy))
        {
            NcfEstadoTexto = $"⚠ La secuencia venció el {secuencia.Vencimiento:dd/MM/yyyy}. " +
                             "Solicitá una nueva a la DGII: hasta entonces los cobros van a fallar.";
            return;
        }
        if (secuencia.EstaAgotada)
        {
            NcfEstadoTexto = "⚠ La secuencia se agotó (fin del rango autorizado). " +
                             "Solicitá una nueva a la DGII: hasta entonces los cobros van a fallar.";
            return;
        }

        NcfEstadoTexto = $"Próximo comprobante: {secuencia.Formatear(secuencia.Proxima)}";
        if (secuencia.Restantes is { } restantes)
            NcfEstadoTexto += restantes <= 20
                ? $" — ⚠ quedan solo {restantes}"
                : $" — quedan {restantes}";
        if (secuencia.Vencimiento is { } v)
            NcfEstadoTexto += $" · vence {v:dd/MM/yyyy}";
    }

    [RelayCommand]
    private async Task GuardarNcfAsync()
    {
        const string titulo = "Comprobante fiscal";
        try
        {
            // Apagar la numeración es un camino propio: no exige que los campos
            // tengan sentido, porque justamente se están dejando de usar.
            if (!NcfActivo)
            {
                await _ncf.DesactivarAsync();
                await CargarNcfAsync();
                _dialogos.Informar(titulo,
                    "Numeración automática apagada. El NCF vuelve a escribirse a mano en cada cobro.");
                return;
            }

            if (!int.TryParse(NcfLargoTexto, NumberStyles.Integer, CulturaDo, out var largo))
            {
                _dialogos.MostrarError(titulo,
                    "El largo de la secuencia debe ser un número (8 tradicional, 10 e-CF).");
                return;
            }
            if (!long.TryParse(NcfProximaTexto, NumberStyles.Integer, CulturaDo, out var proxima))
            {
                _dialogos.MostrarError(titulo, "La próxima secuencia debe ser un número.");
                return;
            }
            long? fin = null;
            if (!string.IsNullOrWhiteSpace(NcfFinTexto))
            {
                if (!long.TryParse(NcfFinTexto, NumberStyles.Integer, CulturaDo, out var f))
                {
                    _dialogos.MostrarError(titulo, "El fin del rango debe ser un número (o quedar vacío).");
                    return;
                }
                fin = f;
            }

            var secuencia = new NcfSecuencia
            {
                Prefijo = NcfPrefijo,
                Largo = largo,
                Proxima = proxima,
                FinRango = fin,
                Vencimiento = NcfVencimiento is { } v ? DateOnly.FromDateTime(v) : null,
                Activo = true
            };
            await _ncf.GuardarSecuenciaAsync(secuencia);
            ActualizarEstadoNcf(secuencia);
            _dialogos.Informar(titulo,
                $"Listo. Desde ahora, si dejás el NCF vacío al cobrar, la app asigna " +
                $"{secuencia.Formatear(secuencia.Proxima)} y sigue de ahí.");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError(titulo, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando la secuencia NCF");
            _dialogos.MostrarError(titulo, $"No se pudo guardar.\n\n{ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Guardar
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task GuardarAsync()
    {
        MensajeError = MensajeExito = string.Empty;

        if (!decimal.TryParse(ItbisTasaTexto, NumberStyles.Number, CulturaDo, out var tasa))
        {
            MensajeError = "La tasa de ITBIS no es un número válido (ej: 18).";
            return;
        }
        if (!int.TryParse(CopiasTicketTexto, NumberStyles.Integer, CulturaDo, out var copias) ||
            copias is < 1 or > 3)
        {
            MensajeError = "Las copias del ticket deben ser 1, 2 o 3.";
            return;
        }
        if (!int.TryParse(ExportCadaDiasTexto, NumberStyles.Integer, CulturaDo, out var dias) || dias < 1)
        {
            MensajeError = "El export automático debe correr cada 1 día o más.";
            return;
        }
        if (ExportAutomaticoActivo && string.IsNullOrWhiteSpace(ExportCarpeta))
        {
            MensajeError = "Elige la carpeta donde se guardarán los archivos de Excel.";
            return;
        }

        var actual = _config.Actual;
        var nueva = new ConfiguracionNegocio
        {
            NombreNegocio = NombreNegocio.Trim(),
            Rnc = string.IsNullOrWhiteSpace(Rnc) ? null : Rnc.Trim(),
            Direccion = string.IsNullOrWhiteSpace(Direccion) ? null : Direccion.Trim(),
            Telefono = string.IsNullOrWhiteSpace(Telefono) ? null : Telefono.Trim(),
            Email = actual.Email,
            LogoRuta = actual.LogoRuta,
            ItbisActivo = ItbisActivo,
            ItbisTasa = tasa,
            Redondeo = RedondeoSeleccionado.Valor,
            MonedaSimbolo = actual.MonedaSimbolo,
            FormatoMiles = actual.FormatoMiles,
            FacturaPrefijo = actual.FacturaPrefijo,
            FacturaSiguiente = actual.FacturaSiguiente,   // lo mueve solo la venta
            FacturaFormato = actual.FacturaFormato,
            MostrarClienteEnVenta = MostrarClienteEnVenta,
            ArsActivo = ArsActivo,
            ArchivarFacturaPdf = ArchivarFacturaPdf,
            ArchivarTurnoPdf = ArchivarTurnoPdf,
            TurnoPrefijo = TurnoPrefijo?.Trim() ?? string.Empty,
            TurnoImprimirAuto = TurnoImprimirAuto
        };

        try
        {
            Ocupado = true;
            await _config.GuardarAsync(nueva);

            var escalaAnterior = _ajustes.FactorEscala;
            _ajustes.TamanoTexto = TamanoTextoSeleccionado.Valor;
            _ajustes.MostrarVistaPreviaTicket = MostrarVistaPreviaTicket;
            _ajustes.CopiasTicket = copias;
            _ajustes.TicketPie = string.IsNullOrWhiteSpace(TicketPie) ? null : TicketPie.Trim();
            _ajustes.CierreAutomaticoActivo = CierreAutomaticoActivo;
            _ajustes.CierreAutomaticoHora = HoraCierreSeleccionada.Valor;
            _ajustes.ExportAutomaticoActivo = ExportAutomaticoActivo;
            _ajustes.ExportAutomaticoCadaDias = dias;
            _ajustes.ExportAutomaticoCarpeta =
                string.IsNullOrWhiteSpace(ExportCarpeta) ? null : ExportCarpeta.Trim();
            // Si quedó apuntando al valor por defecto, se guarda NULL: así, si
            // mañana se mueve la instalación, la carpeta sigue el ejecutable.
            var porDefecto = System.IO.Path.Combine(AppContext.BaseDirectory, "expedientes");
            _ajustes.CarpetaExpedientes =
                string.IsNullOrWhiteSpace(CarpetaExpedientes) ||
                string.Equals(CarpetaExpedientes.TrimEnd(Path.DirectorySeparatorChar),
                              porDefecto.TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase)
                    ? null
                    : CarpetaExpedientes.Trim();
            _ajustes.Guardar();

            // El tamaño de texto se aplica al instante (sin reiniciar la app)
            if (Math.Abs(_ajustes.FactorEscala - escalaAnterior) > 0.001)
                EscalaCambiada?.Invoke(_ajustes.FactorEscala);

            MensajeExito = "Configuración guardada.";
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando la configuración");
            _dialogos.MostrarError("Configuración", $"No se pudo guardar.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task DescartarAsync() => await RefrescarAsync();

    // ------------------------------------------------------------------
    // Respaldo / restauración (la View provee las rutas con los diálogos)
    // ------------------------------------------------------------------

    public async Task RespaldarAsync(string rutaDestino)
    {
        try
        {
            Ocupado = true;
            MensajeError = MensajeExito = string.Empty;
            await _respaldos.RespaldarAsync(rutaDestino);

            // Los PAPELES del paciente van aparte, en su propio ZIP: el .sql
            // solo trae la base, y restaurar sin los archivos dejaría cada
            // expediente apuntando a la nada. Es el mismo criterio que FAControl.
            var zipExpedientes = ExpedienteService.RespaldarTodoEnZip(
                ExpedienteService.CarpetaRaiz(_ajustes),
                Path.GetDirectoryName(rutaDestino) ?? AppContext.BaseDirectory);

            var detalle = $"Se guardó en:\n{rutaDestino}";
            if (zipExpedientes is not null)
                detalle += $"\n\nY los expedientes de los pacientes en:\n{zipExpedientes}";
            else
                detalle += "\n\n(Todavía no hay expedientes de pacientes que respaldar.)";

            _dialogos.Informar("Respaldo completado",
                detalle + "\n\nGuarda los dos archivos en un USB o la nube: con ellos puedes " +
                "restaurar todo el sistema en otro equipo.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error respaldando la base de datos");
            _dialogos.MostrarError("Respaldo", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    public async Task RestaurarAsync(string rutaArchivo)
    {
        // Doble confirmación: es destructivo (patrón PrestControl)
        if (!_dialogos.Confirmar("Restaurar respaldo",
                "Vas a REEMPLAZAR todos los datos actuales (ventas, productos, clientes, usuarios) " +
                "por los del archivo de respaldo.\n\n¿Continuar?"))
            return;
        if (!_dialogos.Confirmar("Confirmación final",
                "Esta acción NO se puede deshacer y los datos actuales se perderán.\n\n" +
                "¿Restaurar de todas formas?"))
            return;

        try
        {
            Ocupado = true;
            await _respaldos.RestaurarAsync(rutaArchivo);
            await _config.CargarAsync();
            _dialogos.Informar("Restauración completada",
                $"Los datos fueron restaurados. Cierra y vuelve a abrir {AppInfo.Nombre} " +
                "para que todas las pantallas se actualicen.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error restaurando la base de datos");
            _dialogos.MostrarError("Restaurar", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    public async Task ExportarAhoraAsync(string rutaDestino)
    {
        try
        {
            Ocupado = true;
            await _exportacion.ExportarAsync(rutaDestino);
            _dialogos.Informar("Exportación completada", $"Se guardó en:\n{rutaDestino}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando a Excel");
            _dialogos.MostrarError("Exportar a Excel", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    public void EstablecerCarpetaExport(string carpeta) => ExportCarpeta = carpeta;

    // ---------- Carpeta de los expedientes ----------

    /// <summary>Dónde viven los archivos del expediente. Se guarda al aplicar.</summary>
    [ObservableProperty] private string _carpetaExpedientes = string.Empty;

    /// <summary>
    /// Cambia la carpeta. Los archivos que YA están no se mueven: moverlos sería
    /// una operación larga y riesgosa que nadie pidió, y sus rutas están guardadas.
    /// Lo que cambia es dónde se guardan los nuevos.
    /// </summary>
    public void EstablecerCarpetaExpedientes(string carpeta) => CarpetaExpedientes = carpeta;

    // ---------- Aviso de caducidad por correo (portado de la suite) ----------
    // UN correo al DUEÑO con la mercancía caducada y la que está por caducar.
    // A los clientes no se les escribe: en el punto de venta no queda nada por
    // cobrar; lo que corre riesgo es el inventario.

    [ObservableProperty] private bool _recordatoriosActivos;
    [ObservableProperty] private bool _recordatoriosAutomaticos;
    [ObservableProperty] private string _gmailRemitente = string.Empty;
    [ObservableProperty] private string _correoDueno = string.Empty;
    [ObservableProperty] private bool _hayAppPasswordGuardada;
    [ObservableProperty] private string _mensajeCorreo = string.Empty;
    [ObservableProperty] private bool _ocupadoCorreo;
    [ObservableProperty] private string _ultimoAvisoTexto = string.Empty;

    /// <summary>
    /// La contraseña recién escrita. La PasswordBox no se puede bindear por
    /// seguridad, así que la View la deposita acá.
    /// </summary>
    public string GmailAppPassword { get; set; } = string.Empty;

    // ---------- Recordatorio de cita al paciente ----------
    [ObservableProperty] private bool _recordatorioCitasActivo;
    [ObservableProperty] private string _recordatorioCitasHorasTexto = "24";
    [ObservableProperty] private string _mensajeCitas = string.Empty;

    partial void OnRecordatorioCitasActivoChanged(bool value) => GuardarAjustesCorreo();
    partial void OnRecordatorioCitasHorasTextoChanged(string value) => GuardarAjustesCorreo();

    // ---------- Paciente que dejó de venir (pedido del cliente 2026-08-25) ----------
    [ObservableProperty] private bool _avisoPacienteInactivoActivo = true;
    [ObservableProperty] private string _avisoPacienteInactivoMesesTexto = "6";

    partial void OnAvisoPacienteInactivoActivoChanged(bool value) => GuardarAvisoInactivo();
    partial void OnAvisoPacienteInactivoMesesTextoChanged(string value) => GuardarAvisoInactivo();

    /// <summary>
    /// Va aparte de GuardarAjustesCorreo: este aviso NO manda correo, se ve
    /// dentro de la aplicación (en Expedientes). Mezclarlos haría que tocar el
    /// corte de meses reescribiera la configuración de Gmail.
    /// </summary>
    private void GuardarAvisoInactivo()
    {
        _ajustes.AvisoPacienteInactivoActivo = AvisoPacienteInactivoActivo;
        // Si escriben cualquier cosa se deja el valor anterior: cero meses
        // marcaría a TODO el mundo como perdido apenas sale por la puerta.
        if (int.TryParse(AvisoPacienteInactivoMesesTexto, out var meses) && meses > 0)
            _ajustes.AvisoPacienteInactivoMeses = Math.Clamp(meses, 1, 60);
        _ajustes.Guardar();
    }

    partial void OnRecordatoriosActivosChanged(bool value) => GuardarAjustesCorreo();
    partial void OnRecordatoriosAutomaticosChanged(bool value) => GuardarAjustesCorreo();
    partial void OnGmailRemitenteChanged(string value) => GuardarAjustesCorreo();
    partial void OnCorreoDuenoChanged(string value) => GuardarAjustesCorreo();

    // ---- Fiados en el aviso diario (012) ----
    [ObservableProperty] private bool _avisoFiadosActivo = true;
    [ObservableProperty] private string _avisoFiadosDiasTexto = "7";

    partial void OnAvisoFiadosActivoChanged(bool value) => GuardarAjustesCorreo();

    /// <summary>
    /// Un texto que todavía no es un número no se guarda ni se corrige solo: si
    /// escribiera un default mientras el usuario borra para reescribir, el
    /// campo pelearía con los dedos. Se guarda cuando vuelve a ser válido.
    /// </summary>
    partial void OnAvisoFiadosDiasTextoChanged(string value)
    {
        if (int.TryParse(value, out var dias) && dias is >= 0 and <= 365)
            GuardarAjustesCorreo();
    }

    private void GuardarAjustesCorreo()
    {
        _ajustes.RecordatoriosActivos = RecordatoriosActivos;
        _ajustes.RecordatoriosAutomaticos = RecordatoriosAutomaticos;
        _ajustes.GmailRemitente = GmailRemitente?.Trim() ?? string.Empty;
        _ajustes.CorreoDueno = CorreoDueno?.Trim() ?? string.Empty;
        _ajustes.RecordatorioCitasActivo = RecordatorioCitasActivo;
        // Si escriben cualquier cosa se deja el valor anterior en vez de poner
        // un cero: cero horas de anticipacion equivale a no avisar nunca.
        if (int.TryParse(RecordatorioCitasHorasTexto, out var horas) && horas > 0)
            _ajustes.RecordatorioCitasHorasAntes = Math.Clamp(horas, 1, 168);

        // Fiados (012). Acá 0 SÍ es válido, al revés que las horas de la cita:
        // significa "avisame solo de las que ya están atrasadas".
        _ajustes.AvisoFiadosActivo = AvisoFiadosActivo;
        if (int.TryParse(AvisoFiadosDiasTexto, out var diasFiado) && diasFiado >= 0)
            _ajustes.AvisoFiadosDias = Math.Clamp(diasFiado, 0, 365);

        _ajustes.Guardar();
    }

    /// <summary>
    /// Guarda la contraseña recién escrita. Se le quitan los ESPACIOS: Google la
    /// muestra como "abcd efgh ijkl mnop" y, pegada con espacios, la
    /// autenticación falla — es la causa número uno de "no me manda el correo".
    /// </summary>
    public void EstablecerAppPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return;
        _ajustes.GmailAppPassword = password.Replace(" ", string.Empty);
        _ajustes.Guardar();
        HayAppPasswordGuardada = true;
    }

    /// <summary>Manda el aviso AHORA, para probar que la cuenta quedó bien.</summary>
    [RelayCommand]
    private async Task EnviarAvisoAsync()
    {
        MensajeCorreo = string.Empty;
        try
        {
            OcupadoCorreo = true;
            var r = await _avisos.EnviarAsync();
            ActualizarUltimoAviso();
            MensajeCorreo = r.Total == 0
                ? r.Detalle
                : $"{r.Caducados} caducado(s) y {r.PorCaducar} por caducar. {r.Detalle}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo el envio manual del aviso de caducidad");
            MensajeCorreo = ex.Message;
        }
        finally
        {
            OcupadoCorreo = false;
        }
    }

    /// <summary>Manda los recordatorios de cita pendientes, sin esperar al automatico.</summary>
    [RelayCommand]
    private async Task EnviarRecordatorioCitasAsync()
    {
        MensajeCitas = string.Empty;
        try
        {
            OcupadoCorreo = true;
            var r = await _recordatorioCitas.EnviarAsync();
            MensajeCitas = r.Detalle;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo el envio manual de recordatorios de cita");
            MensajeCitas = ex.Message;
        }
        finally
        {
            OcupadoCorreo = false;
        }
    }

    private void ActualizarUltimoAviso() =>
        UltimoAvisoTexto = _ajustes.UltimoRecordatorioUtc is { } fecha
            ? $"Último envío: {FechaNegocio.AUtcLocal(fecha):dd/MM/yyyy hh:mm tt}"
            : "Todavía no se envió ningún aviso.";
}
