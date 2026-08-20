using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila de la tabla de pacientes.</summary>
public record ClienteFila(Cliente Cliente)
{
    public long Id => Cliente.Id;
    public string CedulaTexto => string.IsNullOrWhiteSpace(Cliente.Cedula) ? "—" : Cliente.Cedula;
    public string Nombre => Cliente.Nombre;
    public string TelefonoTexto => string.IsNullOrWhiteSpace(Cliente.Telefono) ? "—" : Cliente.Telefono;
    public string EmailTexto => string.IsNullOrWhiteSpace(Cliente.Email) ? "—" : Cliente.Email;
    public string DireccionTexto => string.IsNullOrWhiteSpace(Cliente.Direccion) ? "—" : Cliente.Direccion;

    /// <summary>Edad calculada, no guardada: guardarla la dejaría vieja al día siguiente.</summary>
    public string EdadTexto => EdadPaciente.Texto(Cliente.FechaNacimiento);

    public string SexoTexto => Cliente.Sexo switch
    {
        SexoPaciente.Femenino => "F",
        SexoPaciente.Masculino => "M",
        SexoPaciente.Otro => "Otro",
        _ => "—"
    };

    public string ReferidorTexto =>
        string.IsNullOrWhiteSpace(Cliente.ReferidorNombre) ? "—" : Cliente.ReferidorNombre;

    /// <summary>Sin correo no hay recordatorio de cita. La lista lo marca en gris.</summary>
    public bool TieneEmail => !string.IsNullOrWhiteSpace(Cliente.Email);
}

/// <summary>
/// Lista de pacientes con búsqueda. Los botones de edición/eliminación solo
/// aparecen con permiso clientes_editar (el Cajero consulta, spec §6).
/// </summary>
public partial class ClientesViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly ClienteService _servicio;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<Cliente> _todos = [];

    public event Action? NuevoSolicitado;
    public event Action<long>? EdicionSolicitada;
    /// <summary>"Ver detalles": el shell abre la ficha con el historial.</summary>
    public event Action<long>? FichaSolicitada;

    public ClientesViewModel(ClienteService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    public ObservableCollection<ClienteFila> Filas { get; } = [];

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private string _contadorTexto = string.Empty;
    /// <summary>La fila marcada. Es lo que abre el doble clic.</summary>
    [ObservableProperty] private ClienteFila? _filaSeleccionada;

    public bool PuedeEditar => SesionActual.TienePermiso("clientes_editar");

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();

    public async Task RefrescarAsync()
    {
        try
        {
            OnPropertyChanged(nameof(PuedeEditar));
            _todos = await _servicio.ObtenerTodosAsync();
            AplicarFiltro();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la lista de pacientes");
            _dialogos.MostrarError("Pacientes", $"No se pudo cargar la lista de pacientes.\n\n{ex.Message}");
        }
    }

    private void AplicarFiltro()
    {
        var filtro = TextoBusqueda.Trim();
        // Se busca también por correo y por procedencia: en la recepción el
        // teléfono a veces es lo único que se recuerda, y "¿quién lo mandó?"
        // es la otra pregunta que se hace todos los días.
        var visibles = _todos.Where(c => string.IsNullOrEmpty(filtro) ||
            c.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
            (c.Cedula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.Email?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (c.ReferidorNombre?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false));

        Filas.Clear();
        foreach (var cliente in visibles)
            Filas.Add(new ClienteFila(cliente));

        ContadorTexto = _todos.Count == 0
            ? "Sin pacientes registrados"
            : $"Mostrando {Filas.Count} de {_todos.Count} pacientes";
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    [RelayCommand]
    private void Editar(ClienteFila? fila)
    {
        if (fila is not null)
            EdicionSolicitada?.Invoke(fila.Id);
    }

    /// <summary>Abre la ficha con todo el historial del paciente.</summary>
    [RelayCommand]
    private void VerDetalles(ClienteFila? fila)
    {
        if (fila is not null)
            FichaSolicitada?.Invoke(fila.Id);
    }

    [RelayCommand]
    private async Task EliminarAsync(ClienteFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar paciente",
                $"¿Eliminar a {fila.Nombre}?\n\nSus facturas y citas pasadas se conservan en el historial."))
            return;

        try
        {
            await _servicio.EliminarAsync(fila.Id);
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando paciente {Id}", fila.Id);
            _dialogos.MostrarError("Eliminar paciente", ex.Message);
        }
    }
}
