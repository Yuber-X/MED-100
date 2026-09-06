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
/// Una deuda en la lista, ya con su semáforo resuelto. Es un envoltorio de
/// presentación: el <see cref="FiadoResumen"/> viene del repositorio y no sabe
/// nada de colores ni de cómo se lee una fecha en pantalla.
/// </summary>
public class FiadoFila
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");

    public FiadoFila(FiadoResumen fiado, DateOnly hoy)
    {
        Fiado = fiado;
        Semaforo = CalculadoraFiado.Calcular(fiado.Saldo, fiado.FechaCompromiso, hoy);
        Vencimiento = CalculadoraFiado.DescribirVencimiento(fiado.FechaCompromiso, hoy);
    }

    public FiadoResumen Fiado { get; }
    public SemaforoFiado Semaforo { get; }
    public string Vencimiento { get; }

    public long FacturaId => Fiado.FacturaId;
    public string NumeroFactura => Fiado.NumeroFactura;
    public string ClienteNombre => Fiado.ClienteNombre;
    public string Telefono => string.IsNullOrWhiteSpace(Fiado.ClienteTelefono)
        ? "—" : Fiado.ClienteTelefono!;
    public string SaldoTexto => Fiado.Saldo.ToString("N2", CulturaDo);
    public string PagadoTexto => Fiado.Pagado.ToString("N2", CulturaDo);
    public string TotalTexto => Fiado.PacientePaga.ToString("N2", CulturaDo);
    public string FechaEmisionTexto =>
        FechaNegocio.AUtcLocal(Fiado.FechaEmisionUtc).ToString("dd/MM/yyyy", CulturaDo);
}

/// <summary>
/// Pantalla de fiados (012). Pedido de Yuber del 2026-09-06: el equivalente de
/// las cuotas por vencer de FAControl, pero para lo que los pacientes deben.
///
/// Ver la lista no exige permiso —recepción necesita saber quién debe cuando el
/// paciente llega—; cobrar un abono sí, y eso lo hace cumplir FiadoService, no
/// la pantalla.
/// </summary>
public partial class FiadosViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");

    private readonly FiadoService _fiados;
    private readonly IDialogService _dialogos;

    private IReadOnlyList<FiadoFila> _todas = [];

    public FiadosViewModel(FiadoService fiados, IDialogService dialogos)
    {
        _fiados = fiados;
        _dialogos = dialogos;

        MetodosPago =
        [
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Efectivo, "Efectivo"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Tarjeta, "Tarjeta"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Transferencia, "Transferencia"),
            new Opcion<MetodoPagoFactura>(MetodoPagoFactura.Mixto, "Mixto")
        ];
        _metodoAbono = MetodosPago[0];
    }

    public ObservableCollection<FiadoFila> Fiados { get; } = [];
    public IReadOnlyList<Opcion<MetodoPagoFactura>> MetodosPago { get; }

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private bool _soloAtrasados;
    [ObservableProperty] private FiadoFila? _seleccionada;
    [ObservableProperty] private bool _ocupado;

    // ---- Cobro de un abono ----
    [ObservableProperty] private string _montoAbono = string.Empty;
    [ObservableProperty] private Opcion<MetodoPagoFactura> _metodoAbono;
    [ObservableProperty] private string _notasAbono = string.Empty;

    public bool PuedeCobrar => SesionActual.TienePermiso("fiados");
    public bool HaySeleccion => Seleccionada is not null;

    /// <summary>Total que la clínica tiene afuera, con los filtros aplicados.</summary>
    public string TotalPendienteTexto =>
        Fiados.Sum(f => f.Fiado.Saldo).ToString("N2", CulturaDo);

    public int CantidadAtrasados =>
        Fiados.Count(f => f.Semaforo is SemaforoFiado.Vencido or SemaforoFiado.EnMora);

    partial void OnTextoBusquedaChanged(string value) => Filtrar();
    partial void OnSoloAtrasadosChanged(bool value) => Filtrar();

    partial void OnSeleccionadaChanged(FiadoFila? value)
    {
        OnPropertyChanged(nameof(HaySeleccion));
        // El monto arranca en el saldo completo: cobrar todo es lo más común, y
        // rebajarlo es un gesto deliberado del cajero.
        MontoAbono = value?.Fiado.Saldo.ToString("N2", CulturaDo) ?? string.Empty;
        NotasAbono = string.Empty;
    }

    public async Task RefrescarAsync()
    {
        try
        {
            Ocupado = true;
            OnPropertyChanged(nameof(PuedeCobrar));   // cambia con quién entró

            var hoy = FechaNegocio.Hoy;
            _todas = (await _fiados.ObtenerPendientesAsync())
                .Select(f => new FiadoFila(f, hoy))
                .ToList();
            Filtrar();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los fiados");
            _dialogos.MostrarError("Fiados", $"No se pudo cargar la lista.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void Filtrar()
    {
        var texto = TextoBusqueda.Trim();
        var seleccionadaId = Seleccionada?.FacturaId;

        var filtradas = _todas.AsEnumerable();

        if (texto.Length > 0)
            filtradas = filtradas.Where(f =>
                f.ClienteNombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                f.NumeroFactura.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                (f.Fiado.ClienteTelefono?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false));

        if (SoloAtrasados)
            filtradas = filtradas.Where(f =>
                f.Semaforo is SemaforoFiado.Vencido or SemaforoFiado.EnMora);

        Fiados.Clear();
        foreach (var f in filtradas)
            Fiados.Add(f);

        // Se conserva la selección si sobrevivió al filtro: perderla en cada
        // tecla haría imposible buscar al paciente y después cobrarle.
        Seleccionada = Fiados.FirstOrDefault(f => f.FacturaId == seleccionadaId);

        OnPropertyChanged(nameof(TotalPendienteTexto));
        OnPropertyChanged(nameof(CantidadAtrasados));
    }

    [RelayCommand]
    private async Task CobrarAbonoAsync()
    {
        if (Seleccionada is not { } fila)
            return;

        if (!decimal.TryParse(MontoAbono, NumberStyles.Number, CulturaDo, out var monto))
        {
            _dialogos.MostrarError("Cobrar deuda", "Escribí cuánto está pagando (ej: 500.00).");
            return;
        }

        var queda = fila.Fiado.Saldo - monto;
        var mensaje = queda <= 0m
            ? $"Cobrar RD$ {monto.ToString("N2", CulturaDo)} a {fila.ClienteNombre} " +
              "y dar la deuda por SALDADA?"
            : $"Cobrar RD$ {monto.ToString("N2", CulturaDo)} a {fila.ClienteNombre}? " +
              $"Le quedarían debiendo RD$ {queda.ToString("N2", CulturaDo)}.";

        if (!_dialogos.Confirmar("Cobrar deuda", mensaje))
            return;

        try
        {
            Ocupado = true;
            var saldo = await _fiados.RegistrarAbonoAsync(fila.FacturaId, monto,
                MetodoAbono.Valor, NotasAbono);

            _dialogos.Informar("Cobrar deuda", saldo <= 0m
                ? "Deuda saldada."
                : $"Cobrado. Queda debiendo RD$ {saldo.ToString("N2", CulturaDo)}.");

            await RefrescarAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                      or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Cobrar deuda", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cobrando un abono");
            _dialogos.MostrarError("Cobrar deuda", $"No se pudo registrar el pago.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Mueve la fecha acordada. Es el clásico "pasá el viernes": sin esto, la
    /// única forma de sacar una deuda de la lista de atrasadas sería cobrarla.
    /// </summary>
    [RelayCommand]
    private async Task MoverFechaAsync()
    {
        if (Seleccionada is not { } fila)
            return;

        var texto = _dialogos.PedirTexto("Mover la fecha de pago",
            $"¿Para cuándo queda {fila.ClienteNombre}? Escribí la fecha (dd/mm/aaaa).",
            FechaNegocio.Hoy.AddDays(7).ToString("dd/MM/yyyy", CulturaDo));
        if (string.IsNullOrWhiteSpace(texto))
            return;

        if (!DateTime.TryParseExact(texto.Trim(), "dd/MM/yyyy", CulturaDo,
                DateTimeStyles.None, out var fecha))
        {
            _dialogos.MostrarError("Mover la fecha de pago",
                "No entendí la fecha. Escribila como dd/mm/aaaa, por ejemplo 25/12/2026.");
            return;
        }

        try
        {
            Ocupado = true;
            await _fiados.ActualizarCompromisoAsync(fila.FacturaId, DateOnly.FromDateTime(fecha));
            await RefrescarAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Mover la fecha de pago", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
