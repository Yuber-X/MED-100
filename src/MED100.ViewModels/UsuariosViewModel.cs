using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila de la lista de usuarios.</summary>
public record UsuarioFila(Usuario Usuario)
{
    public long Id => Usuario.Id;
    public string Username => Usuario.Username;
    public string NombreCompleto => Usuario.NombreCompleto;
    public string RolTexto => Usuario.RolNombre ?? "(sin rol)";
    public string EstadoTexto => Usuario.Activo ? "Activo" : "Inactivo";
    public string UltimoAccesoTexto => Usuario.LastLoginAtUtc is { } u
        ? FechaNegocio.AUtcLocal(u).ToString("dd/MM/yyyy hh:mm tt")
        : "Nunca";
    public bool EsElActual => Usuario.Id == SesionActual.Id;
}

/// <summary>Casilla de un permiso en el formulario (marcable por el Admin).</summary>
public partial class PermisoCasilla : ObservableObject
{
    public required string Codigo { get; init; }
    public required string Nombre { get; init; }
    public string? Descripcion { get; init; }

    [ObservableProperty] private bool _asignado;
    /// <summary>True si el rol lo otorga por defecto (se muestra como pista).</summary>
    [ObservableProperty] private bool _vieneDelRol;

    public string PistaTexto => VieneDelRol ? "Por defecto en este rol" : "Permiso adicional";
}

/// <summary>
/// Admin de Usuarios — SOLO Admin (regla Yuber 2026-07-12): crear empleados,
/// restablecer sus contraseñas y ajustar sus permisos desde la misma pantalla.
/// Los permisos por defecto los da el rol (triggers de la BD); las casillas
/// permiten afinar quién puede editar/eliminar productos o clientes.
/// </summary>
public partial class UsuariosViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly UsuarioService _usuarios;
    private readonly IDialogService _dialogos;

    private long? _editandoId;          // null = nuevo
    private IReadOnlyList<Rol> _roles = [];
    private IReadOnlyList<Permiso> _catalogo = [];

    public UsuariosViewModel(UsuarioService usuarios, IDialogService dialogos)
    {
        _usuarios = usuarios;
        _dialogos = dialogos;
    }

    public ObservableCollection<UsuarioFila> Filas { get; } = [];
    public ObservableCollection<Opcion<int>> Roles { get; } = [];
    public ObservableCollection<PermisoCasilla> Permisos { get; } = [];

    [ObservableProperty] private bool _formularioVisible;
    [ObservableProperty] private string _tituloFormulario = "Nuevo usuario";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _apellido = string.Empty;
    [ObservableProperty] private Opcion<int>? _rolSeleccionado;
    [ObservableProperty] private bool _activo = true;
    [ObservableProperty] private bool _esNuevo = true;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private string _mensajeExito = string.Empty;
    [ObservableProperty] private bool _ocupado;

    /// <summary>La contraseña se pide en la View (PasswordBox no se puede bindear).</summary>
    public string PasswordNueva { get; set; } = string.Empty;

    /// <summary>
    /// Qué hace el rol elegido, en una línea. Los cuatro roles no son obvios
    /// por el nombre —sobre todo "Servicio", que atiende pero no cobra— y sin
    /// esto hay que abrir la lista de permisos para adivinarlo.
    /// </summary>
    [ObservableProperty] private string _descripcionRol = string.Empty;

    partial void OnRolSeleccionadoChanged(Opcion<int>? value)
    {
        DescripcionRol = value is null
            ? string.Empty
            : _roles.FirstOrDefault(r => r.Id == value.Valor)?.Descripcion ?? string.Empty;
        _ = MarcarPermisosDelRolAsync(value);
    }

    public async Task RefrescarAsync()
    {
        try
        {
            if (Roles.Count == 0)
            {
                _roles = await _usuarios.ObtenerRolesAsync();
                foreach (var rol in _roles)
                    Roles.Add(new Opcion<int>(rol.Id, rol.Nombre));

                _catalogo = await _usuarios.ObtenerCatalogoPermisosAsync();
            }

            var usuarios = await _usuarios.ObtenerTodosAsync();
            Filas.Clear();
            foreach (var usuario in usuarios)
                Filas.Add(new UsuarioFila(usuario));

            FormularioVisible = false;
            MensajeError = MensajeExito = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando usuarios");
            _dialogos.MostrarError("Usuarios", ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Formulario
    // ------------------------------------------------------------------

    [RelayCommand]
    private void Nuevo()
    {
        _editandoId = null;
        EsNuevo = true;
        TituloFormulario = "Nuevo usuario";
        Username = Nombre = Apellido = string.Empty;
        PasswordNueva = string.Empty;
        Activo = true;
        RolSeleccionado = Roles.FirstOrDefault(r => r.Etiqueta == "Cajero") ?? Roles.FirstOrDefault();
        MensajeError = MensajeExito = string.Empty;
        FormularioVisible = true;
    }

    [RelayCommand]
    private async Task EditarAsync(UsuarioFila? fila)
    {
        if (fila is null)
            return;

        _editandoId = fila.Id;
        EsNuevo = false;
        TituloFormulario = $"Editar usuario — {fila.NombreCompleto}";
        Username = fila.Usuario.Username;
        Nombre = fila.Usuario.Nombre;
        Apellido = fila.Usuario.Apellido ?? string.Empty;
        Activo = fila.Usuario.Activo;
        PasswordNueva = string.Empty;
        MensajeError = MensajeExito = string.Empty;

        RolSeleccionado = Roles.FirstOrDefault(r => r.Valor == fila.Usuario.RolId);

        // Permisos EFECTIVOS del usuario (rol + overrides), no los del rol
        var actuales = await _usuarios.ObtenerPermisosAsync(fila.Id);
        var delRol = fila.Usuario.RolId is { } rolId
            ? await _usuarios.ObtenerPermisosDeRolAsync(rolId)
            : [];

        Permisos.Clear();
        foreach (var permiso in _catalogo)
            Permisos.Add(new PermisoCasilla
            {
                Codigo = permiso.Codigo,
                Nombre = permiso.Nombre,
                Descripcion = permiso.Descripcion,
                Asignado = actuales.Contains(permiso.Codigo),
                VieneDelRol = delRol.Contains(permiso.Codigo)
            });

        FormularioVisible = true;
    }

    /// <summary>Al elegir rol en el formulario, se premarcan sus permisos por defecto.</summary>
    private async Task MarcarPermisosDelRolAsync(Opcion<int>? rol)
    {
        if (rol is null)
            return;

        try
        {
            var delRol = await _usuarios.ObtenerPermisosDeRolAsync(rol.Valor);

            if (Permisos.Count == 0)
                foreach (var permiso in _catalogo)
                    Permisos.Add(new PermisoCasilla
                    {
                        Codigo = permiso.Codigo,
                        Nombre = permiso.Nombre,
                        Descripcion = permiso.Descripcion
                    });

            foreach (var casilla in Permisos)
            {
                casilla.VieneDelRol = delRol.Contains(casilla.Codigo);
                // En alta nueva (o al cambiar de rol) se parte de los del rol
                if (EsNuevo)
                    casilla.Asignado = casilla.VieneDelRol;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los permisos del rol {Rol}", rol.Etiqueta);
        }
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        MensajeError = MensajeExito = string.Empty;

        if (RolSeleccionado is null)
        {
            MensajeError = "Elige un rol para el usuario.";
            return;
        }

        try
        {
            Ocupado = true;
            long id;

            if (_editandoId is null)
            {
                id = await _usuarios.CrearAsync(Username, Nombre, Apellido,
                    RolSeleccionado.Valor, PasswordNueva);
            }
            else
            {
                id = _editandoId.Value;
                await _usuarios.ActualizarAsync(id, Username, Nombre, Apellido,
                    RolSeleccionado.Valor, Activo);

                // La contraseña solo se toca si el Admin escribió una nueva
                if (!string.IsNullOrEmpty(PasswordNueva))
                    await _usuarios.CambiarPasswordAsync(id, PasswordNueva);
            }

            // Permisos finales (el rol ya los sembró; esto aplica los ajustes del Admin)
            var codigos = Permisos.Where(p => p.Asignado).Select(p => p.Codigo).ToList();
            if (codigos.Count > 0)
                await _usuarios.GuardarPermisosAsync(id, codigos);

            PasswordNueva = string.Empty;
            await RefrescarAsync();
            MensajeExito = _editandoId is null
                ? "Usuario creado. Ya puede iniciar sesión."
                : "Usuario actualizado.";
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el usuario");
            _dialogos.MostrarError("Usuarios", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar()
    {
        FormularioVisible = false;
        PasswordNueva = string.Empty;
        MensajeError = string.Empty;
    }

    /// <summary>Devuelve los permisos a los que da el rol (deshace los overrides).</summary>
    [RelayCommand]
    private async Task RestablecerPermisosAsync()
    {
        if (RolSeleccionado is null)
            return;

        var delRol = await _usuarios.ObtenerPermisosDeRolAsync(RolSeleccionado.Valor);
        foreach (var casilla in Permisos)
        {
            casilla.VieneDelRol = delRol.Contains(casilla.Codigo);
            casilla.Asignado = casilla.VieneDelRol;
        }
        MensajeExito = $"Permisos restablecidos a los de {RolSeleccionado.Etiqueta}. " +
                       "Recuerda guardar.";
    }
}
