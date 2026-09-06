using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// Un resultado del buscador: puede ser un PROCEDIMIENTO del tarifario o un
/// INSUMO del inventario. Se unifican en una sola lista porque la recepción
/// escribe "consulta" o "gasa" sin pensar en qué tabla vive cada cosa.
/// </summary>
public record ResultadoCobro(
    long Id, string Nombre, decimal Precio, bool EsProcedimiento, bool Exento, int Stock)
{
    /// <summary>Lo que va debajo del nombre: stock si es insumo, "servicio" si no.</summary>
    public string DetalleTexto => EsProcedimiento
        ? (Exento ? "Procedimiento · exento" : "Procedimiento · gravado")
        : $"Insumo · quedan {Stock}";
}

/// <summary>
/// Línea del carrito en pantalla. La cantidad se ajusta con +/− y el precio se
/// puede rebajar si el usuario tiene el permiso <c>precio_editar</c>.
/// </summary>
public partial class CarritoLinea : ObservableObject
{
    /// <summary>Uno de los dos va con valor, nunca los dos ni ninguno.</summary>
    public long? ProductoId { get; init; }
    public long? ProcedimientoId { get; init; }
    public required string Nombre { get; init; }
    /// <summary>El precio del tarifario. No cambia: es contra el que se compara la rebaja.</summary>
    public required decimal PrecioCatalogo { get; init; }
    /// <summary>Solo tiene sentido en insumos. Un procedimiento no sale de un estante.</summary>
    public required int StockDisponible { get; init; }
    public required bool Exento { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtotal))]
    private int _cantidad = 1;

    /// <summary>
    /// Lo que se va a cobrar por unidad. Arranca en el precio del tarifario.
    ///
    /// Pedido de la clínica (2026-08-27): <i>"debe permitir eliminar dicho
    /// procedimiento y hacer una modificación al precio o una rebajas"</i>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtotal))]
    [NotifyPropertyChangedFor(nameof(Rebajado))]
    [NotifyPropertyChangedFor(nameof(DescuentoTexto))]
    private decimal _precio;

    public decimal Subtotal => Cantidad * Precio;
    public bool EsProcedimiento => ProcedimientoId is not null;
    public string TipoTexto => EsProcedimiento ? "Procedimiento" : "Insumo";

    /// <summary>Se le tocó el precio hacia abajo. Se marca en pantalla.</summary>
    public bool Rebajado => Precio < PrecioCatalogo;

    /// <summary>Lo que se le rebajó a esta línea, para mostrar debajo del precio.</summary>
    public string DescuentoTexto => Rebajado
        ? $"antes {PrecioCatalogo.ToString("N2", CultureInfo.GetCultureInfo("es-DO"))}"
        : string.Empty;
}

/// <summary>
/// Pantalla de cobro de la clínica.
///
/// Lo que la separa de un POS común:
///  - una línea puede ser un PROCEDIMIENTO (exento de ITBIS) o un INSUMO;
///  - hay un MÉDICO, y de sus procedimientos sale su honorario;
///  - puede haber una ARS que cubra parte, y lo que cubre NO entra a la caja.
///
/// Los cálculos los hacen VentaService y CalculosClinica — este VM orquesta.
/// </summary>
public partial class VenderViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");

    private readonly VentaService _ventas;
    private readonly ProductoService _productos;
    private readonly ProcedimientoService _procedimientos;
    private readonly ClienteService _clientes;
    private readonly MedicoService _medicos;
    private readonly ArsService _ars;
    private readonly ConfiguracionNegocioService _config;
    // Se llama _ncfService y no _ncf porque _ncf ya es el campo que respalda la
    // propiedad observable Ncf (el texto que el cajero escribe a mano).
    private readonly NcfService _ncfService;
    private readonly IDialogService _dialogos;

    private IReadOnlyList<Producto> _catalogoInsumos = [];
    private IReadOnlyList<Procedimiento> _catalogoProcedimientos = [];

    /// <summary>Cita que se está cobrando, si el cobro llegó desde la agenda.</summary>
    private long? _citaId;

    /// <summary>La App abre el TicketWindow al registrarse una venta.</summary>
    public event Action<VentaResultado>? VentaRegistrada;

    public VenderViewModel(VentaService ventas, ProductoService productos,
        ProcedimientoService procedimientos, ClienteService clientes, MedicoService medicos,
        ArsService ars, ConfiguracionNegocioService config, NcfService ncf,
        IDialogService dialogos)
    {
        _ventas = ventas;
        _productos = productos;
        _procedimientos = procedimientos;
        _clientes = clientes;
        _medicos = medicos;
        _ars = ars;
        _config = config;
        _ncfService = ncf;
        _dialogos = dialogos;

        MetodosPago =
        [
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Efectivo, "Efectivo"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Tarjeta, "Tarjeta"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Transferencia, "Transferencia"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Mixto, "Mixto")
        ];
        _metodoSeleccionado = MetodosPago[0];
    }

    public ObservableCollection<ResultadoCobro> Resultados { get; } = [];
    public ObservableCollection<CarritoLinea> Carrito { get; } = [];
    public ObservableCollection<Opcion<long?>> Clientes { get; } = [];
    public ObservableCollection<Opcion<long?>> Medicos { get; } = [];
    public ObservableCollection<Opcion<long?>> Aseguradoras { get; } = [];
    public IReadOnlyList<Opcion<MetodoPagoFactura>> MetodosPago { get; }

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private Opcion<long?>? _clienteSeleccionado;
    [ObservableProperty] private Opcion<long?>? _medicoSeleccionado;
    [ObservableProperty] private Opcion<long?>? _arsSeleccionada;
    [ObservableProperty] private string _arsAutorizacion = string.Empty;
    [ObservableProperty] private string _arsCubiertoTexto = string.Empty;
    [ObservableProperty] private string _ncf = string.Empty;

    /// <summary>
    /// El próximo comprobante de la secuencia autorizada, para mostrarlo debajo
    /// de la caja de NCF. Vacío cuando no hay secuencia cargada, está apagada,
    /// venció o se agotó — en cualquiera de esos casos el NCF se sigue
    /// escribiendo a mano y anunciar un número sería mentir.
    /// </summary>
    [ObservableProperty] private string _proximoNcf = string.Empty;

    public bool HayNcfAutomatico => !string.IsNullOrEmpty(ProximoNcf);

    partial void OnProximoNcfChanged(string value) => OnPropertyChanged(nameof(HayNcfAutomatico));

    // ============================================================
    // Fiado (012) — "queda debiendo"
    // ============================================================
    // Apagado por defecto: el caso normal es que el paciente pague todo, y
    // arrancar con la deuda abierta invitaría a fiar por descuido.

    [ObservableProperty] private bool _dejaSaldoPendiente;
    [ObservableProperty] private string _abonadoTexto = string.Empty;
    [ObservableProperty] private DateTime? _fechaCompromiso;

    /// <summary>La sección ni aparece si el usuario no puede fiar.</summary>
    public bool PuedeFiar => SesionActual.TienePermiso("fiados");

    /// <summary>Lo que quedaría debiendo con lo escrito ahora mismo.</summary>
    public string SaldoPendienteTexto
    {
        get
        {
            if (!DejaSaldoPendiente)
                return string.Empty;
            if (!decimal.TryParse(AbonadoTexto, NumberStyles.Number, CulturaDo, out var entrega))
                entrega = 0m;

            var debe = PacientePaga - entrega;
            if (debe < 0m)
                return "Está entregando más de lo que debe.";
            if (debe == 0m)
                return "No queda saldo: está pagando todo.";
            return $"Queda debiendo RD$ {debe.ToString("N2", CulturaDo)}";
        }
    }

    partial void OnDejaSaldoPendienteChanged(bool value)
    {
        if (value)
        {
            // Arranca en blanco y no en el total: el cajero tiene que escribir
            // lo que de verdad recibió, no confirmar un número que ya estaba.
            AbonadoTexto = string.Empty;
            // Quince días es el plazo que la clínica usa de palabra. Es un punto
            // de partida editable, no una regla.
            FechaCompromiso ??= DateTime.Today.AddDays(15);
        }
        OnPropertyChanged(nameof(SaldoPendienteTexto));
    }

    partial void OnAbonadoTextoChanged(string value) =>
        OnPropertyChanged(nameof(SaldoPendienteTexto));
    [ObservableProperty] private Opcion<MetodoPagoFactura> _metodoSeleccionado;
    [ObservableProperty] private string _efectivoTexto = string.Empty;
    [ObservableProperty] private bool _mostrarCliente = true;
    [ObservableProperty] private bool _mostrarArs = true;
    [ObservableProperty] private decimal _subtotal;
    /// <summary>Lo rebajado en total. Se muestra y se imprime, no se resta: ya
    /// está adentro de los precios de las líneas.</summary>
    [ObservableProperty] private decimal _descuento;
    [ObservableProperty] private decimal _itbis;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private decimal _pacientePaga;
    [ObservableProperty] private string _itbisEtiqueta = "ITBIS";
    [ObservableProperty] private string _cambioTexto = "—";
    [ObservableProperty] private string _avisoCita = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public bool PideEfectivo =>
        MetodoSeleccionado.Valor is MetodoPagoFactura.Efectivo or MetodoPagoFactura.Mixto;

    /// <summary>Solo se muestra la línea del ITBIS si de verdad hay impuesto que cobrar.</summary>
    public bool HayItbis => Itbis > 0m;

    /// <summary>Hay seguro elegido: aparece el desglose de quién paga qué.</summary>
    public bool HayArs => ArsSeleccionada?.Valor is not null;

    /// <summary>El médico es obligatorio si hay procedimientos en el carrito.</summary>
    public bool MedicoObligatorio => Carrito.Any(l => l.EsProcedimiento);

    /// <summary>
    /// Quién puede rebajar un precio. Va aparte de «vender» a propósito: cobrar
    /// y decidir cuánto se cobra son dos responsabilidades distintas. Sin el
    /// permiso, la columna de precio queda de solo lectura.
    /// </summary>
    public bool PuedeEditarPrecio => SesionActual.TienePermiso("precio_editar");

    /// <summary>Hay algo rebajado: aparece la línea de descuento en el resumen.</summary>
    public bool HayDescuento => Descuento > 0m;

    public bool HayCitaEnCobro => _citaId is not null;

    /// <summary>
    /// Hay un cobro empezado que se puede abandonar. Sin esto el botón de
    /// cancelar aparecería siempre encendido sobre una pantalla vacía.
    /// </summary>
    public bool HayCobroEmpezado => Carrito.Count > 0 || _citaId is not null;

    partial void OnTextoBusquedaChanged(string value) => Buscar();
    partial void OnEfectivoTextoChanged(string value) => ActualizarCambio();
    partial void OnArsCubiertoTextoChanged(string value) => Recalcular();

    partial void OnArsSeleccionadaChanged(Opcion<long?>? value)
    {
        OnPropertyChanged(nameof(HayArs));
        if (value?.Valor is null)
        {
            ArsAutorizacion = string.Empty;
            ArsCubiertoTexto = string.Empty;
        }
        Recalcular();
    }

    partial void OnMetodoSeleccionadoChanged(Opcion<MetodoPagoFactura> value)
    {
        OnPropertyChanged(nameof(PideEfectivo));
        ActualizarCambio();
    }

    public async Task RefrescarAsync()
    {
        try
        {
            OnPropertyChanged(nameof(PuedeEditarPrecio));   // cambia con quién entró

            var cfg = _config.Actual;
            MostrarCliente = cfg.MostrarClienteEnVenta;
            MostrarArs = cfg.ArsActivo;
            ItbisEtiqueta = $"ITBIS ({cfg.ItbisTasa:0.##}%)";

            _catalogoInsumos = await _productos.ObtenerTodosAsync();
            _catalogoProcedimientos = await _procedimientos.ObtenerActivosAsync();

            // Nunca tira: ProximoNcfAsync devuelve null ante cualquier problema
            // y el cartel simplemente no aparece. Que falle el marcador no puede
            // dejar sin abrir la pantalla de cobro.
            ProximoNcf = await _ncfService.ProximoNcfAsync() ?? string.Empty;

            if (MostrarCliente)
            {
                var lista = await _clientes.ObtenerTodosAsync();
                Clientes.Clear();
                Clientes.Add(new Opcion<long?>(null, "Consumidor final"));
                foreach (var c in lista)
                    Clientes.Add(new Opcion<long?>(c.Id, c.Nombre));
                ClienteSeleccionado ??= Clientes[0];
            }

            var medicoPrevio = MedicoSeleccionado?.Valor;
            Medicos.Clear();
            Medicos.Add(new Opcion<long?>(null, "Sin médico"));
            foreach (var m in await _medicos.ObtenerActivosAsync())
                Medicos.Add(new Opcion<long?>(m.Id, m.Nombre));
            MedicoSeleccionado = Medicos.FirstOrDefault(m => m.Valor == medicoPrevio) ?? Medicos[0];

            if (MostrarArs)
            {
                var arsPrevia = ArsSeleccionada?.Valor;
                Aseguradoras.Clear();
                Aseguradoras.Add(new Opcion<long?>(null, "Paciente privado"));
                foreach (var a in await _ars.ObtenerActivasAsync())
                    Aseguradoras.Add(new Opcion<long?>(a.Id, a.Nombre));
                ArsSeleccionada = Aseguradoras.FirstOrDefault(a => a.Valor == arsPrevia) ?? Aseguradoras[0];
            }

            Recalcular();   // por si cambió la tasa o la exención desde Configuración
            Buscar();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la pantalla de cobro");
            _dialogos.MostrarError("Cobrar", $"No se pudo cargar el catálogo.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Prepara el cobro de una cita: paciente, médico y procedimiento ya puestos.
    /// Es el camino corto de la agenda a la caja.
    /// </summary>
    public async Task PrepararDesdeCitaAsync(Cita cita)
    {
        await RefrescarAsync();

        Carrito.Clear();
        _citaId = cita.Id;
        ClienteSeleccionado = Clientes.FirstOrDefault(c => c.Valor == cita.ClienteId)
                              ?? Clientes.FirstOrDefault();
        MedicoSeleccionado = Medicos.FirstOrDefault(m => m.Valor == cita.MedicoId)
                             ?? Medicos.FirstOrDefault();

        if (cita.ProcedimientoId is { } procedimientoId)
        {
            var procedimiento = _catalogoProcedimientos.FirstOrDefault(p => p.Id == procedimientoId);
            if (procedimiento is not null)
                Agregar(DeProcedimiento(procedimiento));
        }

        AvisoCita = $"Cobrando la cita de {cita.PacienteNombre} con {cita.MedicoNombre}.";
        OnPropertyChanged(nameof(HayCitaEnCobro));
        Recalcular();
    }

    private static ResultadoCobro DeProcedimiento(Procedimiento p) =>
        new(p.Id, p.Nombre, p.Precio, EsProcedimiento: true, p.ExentoItbis, Stock: int.MaxValue);

    private static ResultadoCobro DeInsumo(Producto p) =>
        // Los insumos NO son servicio de salud: llevan ITBIS si el negocio lo
        // tiene activo. La exención por producto la decide el contador y hoy
        // no se edita desde acá.
        new(p.Id, p.Nombre, p.Precio, EsProcedimiento: false, Exento: false, p.Cantidad);

    private void Buscar()
    {
        var filtro = TextoBusqueda.Trim();
        Resultados.Clear();
        if (string.IsNullOrEmpty(filtro))
            return;

        // Código exacto primero (pistola de código de barras)
        var exacto = _catalogoInsumos.FirstOrDefault(p =>
            string.Equals(p.Codigo, filtro, StringComparison.OrdinalIgnoreCase));
        if (exacto is not null)
        {
            Agregar(DeInsumo(exacto));
            TextoBusqueda = string.Empty;   // listo para el próximo escaneo
            return;
        }

        // Procedimientos primero: en una clínica es lo que más se cobra.
        foreach (var p in _catalogoProcedimientos
                     .Where(p => p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                                 (p.Codigo?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))
                     .Take(6))
            Resultados.Add(DeProcedimiento(p));

        foreach (var p in _catalogoInsumos
                     .Where(p => p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                                 (p.Codigo?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))
                     .Take(6))
            Resultados.Add(DeInsumo(p));
    }

    [RelayCommand]
    private void Agregar(ResultadoCobro? fila)
    {
        if (fila is null)
            return;

        var existente = fila.EsProcedimiento
            ? Carrito.FirstOrDefault(l => l.ProcedimientoId == fila.Id)
            : Carrito.FirstOrDefault(l => l.ProductoId == fila.Id);

        // El stock solo frena a los insumos: de un procedimiento se pueden
        // cobrar tres sesiones sin que haya nada que descontar.
        if (!fila.EsProcedimiento)
        {
            var enCarrito = existente?.Cantidad ?? 0;
            if (enCarrito + 1 > fila.Stock)
            {
                _dialogos.MostrarError("Stock",
                    $"Solo quedan {fila.Stock} unidades de {fila.Nombre}.");
                return;
            }
        }

        if (existente is not null)
        {
            existente.Cantidad++;
        }
        else
        {
            var linea = new CarritoLinea
            {
                ProductoId = fila.EsProcedimiento ? null : fila.Id,
                ProcedimientoId = fila.EsProcedimiento ? fila.Id : null,
                Nombre = fila.Nombre,
                PrecioCatalogo = fila.Precio,
                Precio = fila.Precio,      // arranca en el de lista
                StockDisponible = fila.Stock,
                Exento = fila.Exento
            };
            // Cambiar el precio a mano tiene que recalcular los totales en el
            // acto: si el TOTAL no sigue al precio, la cajera cobra el número
            // viejo y la diferencia aparece en el cuadre del día.
            linea.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CarritoLinea.Precio))
                    Recalcular();
            };
            Carrito.Add(linea);
        }
        Recalcular();
    }

    [RelayCommand]
    private void Mas(CarritoLinea? linea)
    {
        if (linea is null) return;
        if (!linea.EsProcedimiento && linea.Cantidad + 1 > linea.StockDisponible)
        {
            _dialogos.MostrarError("Stock", $"Solo quedan {linea.StockDisponible} unidades de {linea.Nombre}.");
            return;
        }
        linea.Cantidad++;
        Recalcular();
    }

    [RelayCommand]
    private void Menos(CarritoLinea? linea)
    {
        if (linea is null) return;
        if (linea.Cantidad <= 1)
            Carrito.Remove(linea);
        else
            linea.Cantidad--;
        Recalcular();
    }

    [RelayCommand]
    private void Quitar(CarritoLinea? linea)
    {
        if (linea is null) return;
        Carrito.Remove(linea);
        Recalcular();
    }

    private void Recalcular()
    {
        var cfg = _config.Actual;
        var totales = CalculosClinica.CalcularTotales(
            LineasActuales(), cfg.ItbisTasaEfectiva, cfg.Redondeo);

        Subtotal = totales.Subtotal;
        Itbis = totales.Itbis;
        Total = totales.Total;
        Descuento = totales.Descuento;

        var reparto = CalculosClinica.CalcularReparto(totales.Total,
            ArsSeleccionada?.Valor, null, null, LeerCubierto());
        PacientePaga = reparto.PacientePaga;
        // El saldo del fiado se calcula contra esto: si cambia el carrito o la
        // cobertura de la ARS, el "queda debiendo" tiene que moverse con él.
        OnPropertyChanged(nameof(SaldoPendienteTexto));

        OnPropertyChanged(nameof(HayItbis));
        OnPropertyChanged(nameof(HayDescuento));
        OnPropertyChanged(nameof(MedicoObligatorio));
        OnPropertyChanged(nameof(HayCobroEmpezado));
        ActualizarCambio();
    }

    private decimal LeerCubierto() =>
        decimal.TryParse(ArsCubiertoTexto, NumberStyles.Number, CulturaDo, out var monto) && monto > 0m
            ? monto
            : 0m;

    private void ActualizarCambio()
    {
        // Se compara contra lo que paga el PACIENTE, no contra el total: con
        // seguro de por medio son dos números distintos y el que se recibe en
        // el mostrador es el del paciente.
        if (MetodoSeleccionado.Valor == MetodoPagoFactura.Efectivo &&
            decimal.TryParse(EfectivoTexto, NumberStyles.Number, CulturaDo, out var efectivo) &&
            efectivo >= PacientePaga && PacientePaga > 0)
        {
            CambioTexto = VentaService.CalcularCambio(efectivo, PacientePaga).ToString("N2", CulturaDo);
        }
        else
        {
            CambioTexto = "—";
        }
    }

    private List<VentaLinea> LineasActuales() =>
        [.. Carrito.Select(l => new VentaLinea(
            l.ProductoId, l.Nombre, l.Cantidad, l.Precio, l.Exento, l.ProcedimientoId,
            l.PrecioCatalogo))];

    /// <summary>
    /// Abandona el cobro a medio armar y deja la pantalla como recién abierta.
    ///
    /// Pedido del cliente (2026-08-28): <i>"si uno elige algo por error o se
    /// arrepiente. No puede cancelar o darle para atrás"</i>. Se podía quitar
    /// línea por línea, pero no soltar el cobro entero — y menos el paciente y
    /// el médico que arrastra una cita traída desde la agenda.
    ///
    /// No borra nada de la base: acá todavía no hay factura.
    /// </summary>
    [RelayCommand]
    private void CancelarCobro()
    {
        if (!HayCobroEmpezado)
            return;

        var detalle = HayCitaEnCobro
            ? "Se descarta lo cargado y se suelta la cita. La cita NO se pierde: " +
              "queda en la agenda para cobrarla después."
            : "Se descarta todo lo que hay cargado.";

        if (!_dialogos.Confirmar("Cancelar cobro", "¿Empezar de cero?\n\n" + detalle))
            return;

        LimpiarParaElSiguiente();
    }

    [RelayCommand]
    private async Task CobrarAsync()
    {
        if (Carrito.Count == 0)
        {
            _dialogos.MostrarError("Cobrar", "No hay nada que cobrar.");
            return;
        }

        decimal? efectivo = null;
        if (PideEfectivo)
        {
            if (!decimal.TryParse(EfectivoTexto, NumberStyles.Number, CulturaDo, out var monto))
            {
                _dialogos.MostrarError("Cobrar", "Indicá el efectivo recibido (ej: 500.00).");
                return;
            }
            efectivo = monto;
        }

        // ---- Fiado (012) -----------------------------------------------
        decimal? abonado = null;
        DateOnly? compromiso = null;
        if (DejaSaldoPendiente)
        {
            if (!decimal.TryParse(AbonadoTexto, NumberStyles.Number, CulturaDo, out var entrega))
            {
                _dialogos.MostrarError("Cobrar",
                    "Indicá cuánto está entregando el paciente (ej: 500.00). " +
                    "Si no deja nada, escribí 0.");
                return;
            }
            if (FechaCompromiso is not { } fecha)
            {
                _dialogos.MostrarError("Cobrar",
                    "Indicá para cuándo se compromete a pagar. Sin fecha, la deuda no " +
                    "aparece en los avisos y se pierde de vista.");
                return;
            }
            abonado = entrega;
            compromiso = DateOnly.FromDateTime(fecha);
        }

        var solicitud = new VentaSolicitud(LineasActuales(),
            MostrarCliente ? ClienteSeleccionado?.Valor : null,
            MetodoSeleccionado.Valor, efectivo,
            MedicoSeleccionado?.Valor,
            MostrarArs ? ArsSeleccionada?.Valor : null,
            string.IsNullOrWhiteSpace(ArsAutorizacion) ? null : ArsAutorizacion.Trim(),
            MostrarArs ? LeerCubierto() : 0m,
            string.IsNullOrWhiteSpace(Ncf) ? null : Ncf.Trim(),
            _citaId, abonado, compromiso);

        try
        {
            Ocupado = true;
            var resultado = await _ventas.RegistrarVentaAsync(solicitud);

            LimpiarParaElSiguiente();
            await RefrescarAsync();   // stock actualizado para el próximo cobro

            VentaRegistrada?.Invoke(resultado);
        }
        catch (ArgumentException ex)
        {
            _dialogos.MostrarError("Cobrar", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // Stock insuficiente u otra regla: el cobro se revirtió completo
            _dialogos.MostrarError("Cobrar", ex.Message);
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error registrando el cobro");
            _dialogos.MostrarError("Cobrar", $"No se pudo registrar el cobro.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Vuelve a leer el próximo comprobante de la secuencia. Se traga cualquier
    /// error: el cartel es una ayuda visual y el cobro no depende de él.
    /// </summary>
    private async Task RefrescarProximoNcfAsync()
    {
        try
        {
            ProximoNcf = await _ncfService.ProximoNcfAsync() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo refrescar el próximo NCF");
            ProximoNcf = string.Empty;
        }
    }

    private void LimpiarParaElSiguiente()
    {
        Carrito.Clear();
        EfectivoTexto = string.Empty;
        TextoBusqueda = string.Empty;
        ArsAutorizacion = string.Empty;
        ArsCubiertoTexto = string.Empty;
        // El NCF NO se arrastra al próximo cobro: es único por comprobante y
        // repetirlo haría fallar la restricción con un error incomprensible.
        Ncf = string.Empty;
        // El cobro que acaba de salir consumió un número: el cartel tiene que
        // mostrar el siguiente, no el que ya se fue en el papel anterior.
        // Sin await a propósito — el cajero ya puede empezar a cargar el próximo
        // paciente mientras esto vuelve, y si falla el cartel solo desaparece.
        _ = RefrescarProximoNcfAsync();
        // El fiado tampoco se arrastra: el próximo paciente paga lo suyo.
        DejaSaldoPendiente = false;
        AbonadoTexto = string.Empty;
        FechaCompromiso = null;
        AvisoCita = string.Empty;
        _citaId = null;
        ClienteSeleccionado = Clientes.FirstOrDefault();
        ArsSeleccionada = Aseguradoras.FirstOrDefault();
        OnPropertyChanged(nameof(HayCitaEnCobro));
        OnPropertyChanged(nameof(HayCobroEmpezado));
        Recalcular();
    }
}
