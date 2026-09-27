using System.Windows;
using System.Windows.Controls;
using MED100.Models;

namespace MED100.Views;

/// <summary>
/// La lista completa de pacientes para elegir uno (pedido de la clínica
/// 2026-09-21: el botón que "jala" los pacientes desde Citas).
///
/// El combo del formulario muestra los primeros 50 y alcanza cuando se sabe el
/// nombre; acá se busca por cédula o teléfono y se ve a quién se está eligiendo
/// cuando hay dos María Pérez.
/// </summary>
public partial class SelectorPacienteWindow : Window
{
    private readonly IReadOnlyList<Cliente> _todos;

    /// <summary>El paciente elegido, o null si se canceló.</summary>
    public Cliente? Elegido { get; private set; }

    public SelectorPacienteWindow(IReadOnlyList<Cliente> pacientes, string? busquedaInicial = null)
    {
        InitializeComponent();
        _todos = pacientes;
        CajaBusqueda.Text = busquedaInicial ?? string.Empty;
        Filtrar();
        CajaBusqueda.Focus();
        CajaBusqueda.SelectAll();
    }

    private void Busqueda_TextChanged(object sender, TextChangedEventArgs e) => Filtrar();

    private void Filtrar()
    {
        var filtro = CajaBusqueda.Text.Trim();
        var visibles = filtro.Length == 0
            ? _todos
            : [.. _todos.Where(p =>
                p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                (p.Cedula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false))];

        Lista.ItemsSource = visibles;
        TextoResumen.Text = _todos.Count == 0
            ? "Todavía no hay pacientes registrados."
            : visibles.Count == 0
                ? "Ningún paciente coincide con esa búsqueda."
                : $"{visibles.Count} de {_todos.Count} paciente(s)";
        ActualizarBoton();
    }

    private void Lista_SelectionChanged(object sender, SelectionChangedEventArgs e) => ActualizarBoton();

    private void ActualizarBoton() => BotonElegir.IsEnabled = Lista.SelectedItem is Cliente;

    private void Lista_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        Elegir();

    private void Elegir_Click(object sender, RoutedEventArgs e) => Elegir();

    private void Elegir()
    {
        if (Lista.SelectedItem is not Cliente paciente)
            return;
        Elegido = paciente;
        DialogResult = true;
    }
}
