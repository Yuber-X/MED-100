using System.Windows;
using System.Windows.Controls;
using MED100.ViewModels;

namespace MED100.Views;

/// <summary>
/// Code-behind de SOLO UI: WPF no permite bindear PasswordBox.Password
/// (por seguridad), así que la contraseña se pasa al ViewModel a mano.
/// </summary>
public partial class UsuariosView : UserControl
{
    public UsuariosView() => InitializeComponent();

    private UsuariosViewModel? Vm => DataContext as UsuariosViewModel;

    private void CajaPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            Vm.PasswordNueva = CajaPassword.Password;
    }

    private void BotonCancelar_Click(object sender, RoutedEventArgs e) => CajaPassword.Clear();
}
