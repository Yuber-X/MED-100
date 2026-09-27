using System.Windows;
using MED100.ViewModels;

namespace MED100.Views;

public partial class CitasView : System.Windows.Controls.UserControl
{
    public CitasView() => InitializeComponent();

    /// <summary>
    /// Abre la lista completa de pacientes y deja elegido el que se marque
    /// (pedido de la clínica 2026-09-21). La ventana vive acá y no en el
    /// ViewModel: el VM no conoce ventanas, igual que en el resto del proyecto.
    /// </summary>
    private void BuscarPaciente_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CitasViewModel vm)
            return;

        var ventana = new SelectorPacienteWindow(vm.TodosLosPacientes, vm.BusquedaPaciente)
        {
            Owner = Window.GetWindow(this)
        };
        if (ventana.ShowDialog() == true && ventana.Elegido is { } paciente)
            vm.ElegirPaciente(paciente);
    }
}
