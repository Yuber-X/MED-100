using System.Windows;
using MED100.Models;
using MED100.ViewModels;

namespace MED100.Views;

public partial class ClienteFormView : System.Windows.Controls.UserControl
{
    public ClienteFormView() => InitializeComponent();

    /// <summary>
    /// Escanear la cédula (pedido de la clínica 2026-09-21). La ventana vive
    /// acá y no en el ViewModel: el VM no conoce ventanas ni cámaras, igual que
    /// en el resto del proyecto.
    ///
    /// Lo leído solo COMPLETA lo que está vacío —salvo la cédula, que es lo que
    /// se fue a buscar—. Si la recepcionista ya escribió el nombre, manda lo
    /// que escribió: el nombre que trae el código puede venir abreviado o en
    /// otro orden.
    /// </summary>
    private void Escanear_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ClienteFormViewModel vm)
            return;

        var ventana = new EscanearCedulaWindow { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() != true || ventana.Resultado is not { } leido)
            return;

        if (leido.Cedula is { } cedula)
            vm.Cedula = cedula;

        if (leido.Nombre is { } nombre && string.IsNullOrWhiteSpace(vm.Nombre))
            vm.Nombre = nombre;

        if (leido.FechaNacimiento is { } nacimiento && vm.FechaNacimiento is null)
            vm.FechaNacimiento = nacimiento.ToDateTime(TimeOnly.MinValue);

        if (leido.Sexo is { } sexo && vm.SexoSeleccionado?.Valor is null)
            vm.SexoSeleccionado = vm.OpcionesSexo.FirstOrDefault(o => o.Valor == sexo)
                                  ?? vm.SexoSeleccionado;
    }
}
