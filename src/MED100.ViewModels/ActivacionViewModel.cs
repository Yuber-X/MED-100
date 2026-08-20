using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// La pantalla del código. Sale sola cuando se acabó el demo —y ahí no deja
/// pasar— y también a pedido, desde Configuración, para activar antes de que
/// se acabe.
///
/// El mismo ViewModel sirve para los dos casos: la diferencia es
/// <see cref="EsBloqueante"/>, que decide si hay botón de "seguir en demo" o
/// solo el de salir.
/// </summary>
public partial class ActivacionViewModel : ObservableObject
{
    private readonly LicenciaService _licencias;

    public ActivacionViewModel(LicenciaService licencias)
    {
        _licencias = licencias;
        Refrescar();
    }

    /// <summary>La ventana se cierra. true = puede seguir usando la app.</summary>
    public event Action<bool>? Resuelto;

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    [ObservableProperty] private string _titulo = string.Empty;
    [ObservableProperty] private string _explicacion = string.Empty;
    [ObservableProperty] private bool _esBloqueante;
    [ObservableProperty] private bool _pideCodigo = true;

    public string Telefono => Soporte.Telefono;
    public string TextoSoporte =>
        $"Para comprar la llave, escribí o llamá a {Soporte.Desarrollador} al {Soporte.Telefono}.";

    private void Refrescar()
    {
        var estado = _licencias.Estado;
        EsBloqueante = !estado.PermiteEntrar;
        PideCodigo = estado.Estado != EstadoLicencia.Completa;

        (Titulo, Explicacion) = estado.Estado switch
        {
            EstadoLicencia.Completa => (
                "MED-100 está activado",
                "Esta computadora tiene la versión completa. No hay nada que hacer acá."),

            EstadoLicencia.Demo when estado.DiasRestantes == 1 => (
                "Hoy es el último día de prueba",
                "Mañana MED-100 va a pedir la llave para poder abrir. Tus datos no se " +
                "borran ni se pierden: quedan esperando en la base de datos y aparecen " +
                "completos apenas se active."),

            EstadoLicencia.Demo => (
                $"Prueba: quedan {estado.DiasRestantes} días",
                "Podés usar MED-100 completo durante la prueba. Cuando se acabe, la app " +
                "va a pedir la llave para abrir; los datos que cargues siguen ahí."),

            EstadoLicencia.RelojAtrasado => (
                "La fecha de esta computadora está atrasada",
                "MED-100 se abrió por última vez con una fecha posterior a la de hoy. " +
                "Puede ser la pila del reloj o alguien que cambió la fecha a mano. " +
                "Con la llave del producto se resuelve y no vuelve a molestar."),

            _ => (
                "Se acabaron los 15 días de prueba",
                "Para seguir usando MED-100 hay que activarlo con la llave del producto. " +
                "Nada se perdió: los pacientes, las citas y las facturas están completos " +
                "y aparecen apenas se escriba la llave.")
        };
    }

    /// <summary>
    /// Le pone los guiones al código mientras se escribe, así el que lo copia
    /// de un papel ve si va bien encaminado.
    /// </summary>
    partial void OnCodigoChanged(string value)
    {
        MensajeError = string.Empty;
        var formateado = CodigoLicencia.Formatear(value);
        if (formateado != value)
            Codigo = formateado;
    }

    [RelayCommand]
    private async Task ActivarAsync()
    {
        if (Ocupado)
            return;

        Ocupado = true;
        MensajeError = string.Empty;
        try
        {
            if (await _licencias.ActivarAsync(Codigo))
            {
                Refrescar();
                Resuelto?.Invoke(true);
                return;
            }
            MensajeError = "Ese código no es válido. Revisalo y volvé a escribirlo.";
        }
        catch (Exception ex)
        {
            // Base caída justo al activar: el código puede estar bien, así que
            // se dice qué pasó en vez de acusar al código.
            Log.Error(ex, "No se pudo activar la licencia");
            MensajeError = "No se pudo guardar la activación: " + ex.Message;
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Seguir en demo. Solo existe mientras queden días.</summary>
    [RelayCommand]
    private void Continuar() => Resuelto?.Invoke(true);

    /// <summary>Cerrar sin activar cuando ya no se puede entrar.</summary>
    [RelayCommand]
    private void Salir() => Resuelto?.Invoke(false);
}
