using CommunityToolkit.Mvvm.ComponentModel;
using MED100.Services;

namespace MED100.ViewModels;

/// <summary>
/// Login y wizard de cuenta inicial. La contraseña llega como parámetro desde
/// el code-behind del PasswordBox (WPF no permite binding seguro de Password);
/// eso es lógica de UI permitida, la decisión de negocio vive aquí.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _auth;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string _mensajeError = string.Empty;

    [ObservableProperty]
    private bool _esWizardInicial;

    [ObservableProperty]
    private bool _ocupado;

    /// <summary>Lo dispara el VM cuando el login fue exitoso; la View abre el shell.</summary>
    public event EventHandler? LoginExitoso;

    public LoginViewModel(AuthService auth) => _auth = auth;

    /// <summary>Decide si mostrar wizard (primer arranque) o login normal.</summary>
    public async Task InicializarAsync()
    {
        EsWizardInicial = await _auth.RequiereCuentaInicialAsync();
    }

    public async Task IntentarLoginAsync(string password)
    {
        MensajeError = string.Empty;
        Ocupado = true;
        try
        {
            var resultado = await _auth.LoginAsync(Username, password);
            switch (resultado)
            {
                case ResultadoLogin.Exitoso:
                    LoginExitoso?.Invoke(this, EventArgs.Empty);
                    break;
                case ResultadoLogin.BloqueadoTemporalmente:
                    MensajeError = "Demasiados intentos fallidos. Espera 5 minutos e intenta de nuevo.";
                    break;
                default:
                    MensajeError = "Usuario o contraseña incorrectos.";
                    break;
            }
        }
        catch (Exception ex)
        {
            MensajeError = "No se pudo conectar con la base de datos.";
            Serilog.Log.Error(ex, "Error de conexión durante el login");
        }
        finally
        {
            Ocupado = false;
        }
    }

    public async Task CrearCuentaInicialAsync(string password, string confirmacion)
    {
        MensajeError = string.Empty;

        if (password != confirmacion)
        {
            MensajeError = "Las contraseñas no coinciden.";
            return;
        }

        Ocupado = true;
        try
        {
            await _auth.CrearCuentaInicialAsync(Username, Nombre, password);
            // Cuenta creada: login inmediato con las mismas credenciales
            await IntentarLoginAsync(password);
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            MensajeError = "No se pudo crear la cuenta. Revisa la conexión a la base de datos.";
            Serilog.Log.Error(ex, "Error creando cuenta inicial");
        }
        finally
        {
            Ocupado = false;
        }
    }
}
