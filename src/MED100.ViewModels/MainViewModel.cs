using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Services;

namespace MED100.ViewModels;

/// <summary>Páginas del shell. El orden es el del sidebar (spec §7).</summary>
public enum Pagina
{
    Panel,
    Vender,
    Clientes,
    Medicos,
    Procedimientos,
    Citas,
    Turnos,
    Expedientes,
    Productos,
    Almacen,
    Caducidad,
    Comprobantes,
    Cuadre,
    Reportes,
    Usuarios,
    Configuracion
}

/// <summary>
/// Shell principal: navegación + FILTRADO POR PERMISOS (obligatorio, spec §6).
/// Cada ítem del sidebar solo es visible si SesionActual tiene su permiso;
/// Navegar() revalida por si acaso (defensa en profundidad).
/// En Fase 1 todas las páginas son placeholders; se reemplazan por fases.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>Permiso requerido por página (códigos de la tabla permiso).</summary>
    private static readonly Dictionary<Pagina, string> PermisoPorPagina = new()
    {
        [Pagina.Panel] = "panel",
        [Pagina.Vender] = "vender",
        [Pagina.Clientes] = "clientes",
        [Pagina.Medicos] = "medicos",
        [Pagina.Procedimientos] = "procedimientos",
        [Pagina.Citas] = "citas",
        [Pagina.Turnos] = "turnos",
        [Pagina.Expedientes] = "expedientes",
        [Pagina.Productos] = "productos",
        [Pagina.Almacen] = "almacen",
        [Pagina.Caducidad] = "caducidad",
        [Pagina.Comprobantes] = "comprobantes",
        [Pagina.Cuadre] = "cuadre",
        [Pagina.Reportes] = "reportes",
        [Pagina.Usuarios] = "usuarios",
        [Pagina.Configuracion] = "configuracion"   // EXCLUSIVO Admin (regla 2026-07-11)
    };

    private static readonly Dictionary<Pagina, string> Titulos = new()
    {
        [Pagina.Panel] = "Panel",
        [Pagina.Vender] = "Vender",
        [Pagina.Clientes] = "Pacientes",
        [Pagina.Medicos] = "Médicos",
        [Pagina.Procedimientos] = "Procedimientos",
        [Pagina.Citas] = "Citas",
        [Pagina.Turnos] = "Sala de espera",
        [Pagina.Expedientes] = "Almacén de expedientes",
        [Pagina.Productos] = "Productos",
        [Pagina.Almacen] = "Almacén",
        [Pagina.Caducidad] = "Caducidad",
        [Pagina.Comprobantes] = "Buscar comprobante",
        [Pagina.Cuadre] = "Cuadre de caja",
        [Pagina.Reportes] = "Reportes",
        [Pagina.Usuarios] = "Usuarios",
        [Pagina.Configuracion] = "Configuración"
    };

    private readonly IDialogService _dialogos;
    private readonly LicenciaService _licencias;
    private readonly Dictionary<Pagina, object> _paginas;

    [ObservableProperty]
    private Pagina _paginaActual;

    [ObservableProperty]
    private object? _paginaActualVm;

    [ObservableProperty]
    private string _tituloPagina = string.Empty;

    public MainViewModel(IDialogService dialogos, LicenciaService licencias)
    {
        _dialogos = dialogos;
        _licencias = licencias;

        // La pastilla del menú se apaga sola en cuanto se activa desde
        // Configuración: sin esto habría que cerrar y volver a abrir la app
        // para dejar de ver "DEMO".
        _licencias.Cambio += () =>
        {
            OnPropertyChanged(nameof(EnDemo));
            OnPropertyChanged(nameof(EtiquetaLicencia));
        };
        // Fase 1: placeholders. Cada fase sustituye el suyo por el VM real.
        _paginas = Titulos.ToDictionary(
            kv => kv.Key,
            kv => (object)new PlaceholderViewModel(kv.Value));
    }

    // Visibilidad del sidebar según permisos (se leen tras el login)
    public bool PuedeVerPanel => SesionActual.TienePermiso("panel");
    public bool PuedeVerVender => SesionActual.TienePermiso("vender");
    public bool PuedeVerClientes => SesionActual.TienePermiso("clientes");
    public bool PuedeVerMedicos => SesionActual.TienePermiso("medicos");
    public bool PuedeVerProcedimientos => SesionActual.TienePermiso("procedimientos");
    public bool PuedeVerCitas => SesionActual.TienePermiso("citas");
    public bool PuedeVerTurnos => SesionActual.TienePermiso("turnos");
    public bool PuedeVerExpedientes => SesionActual.TienePermiso("expedientes");
    public bool PuedeVerProductos => SesionActual.TienePermiso("productos");
    public bool PuedeVerAlmacen => SesionActual.TienePermiso("almacen");
    public bool PuedeVerCaducidad => SesionActual.TienePermiso("caducidad");
    public bool PuedeVerComprobantes => SesionActual.TienePermiso("comprobantes");
    public bool PuedeVerCuadre => SesionActual.TienePermiso("cuadre");
    public bool PuedeVerReportes => SesionActual.TienePermiso("reportes");
    public bool PuedeVerUsuarios => SesionActual.TienePermiso("usuarios");
    public bool PuedeVerConfiguracion => SesionActual.TienePermiso("configuracion");

    /// <summary>La pastilla de "DEMO · N días" del menú. Desaparece al activar.</summary>
    public bool EnDemo => !_licencias.EstaActivada;
    public string EtiquetaLicencia => _licencias.Estado.Etiqueta;

    public string NombreUsuario => SesionActual.Nombre;
    public string RolUsuario => SesionActual.Rol;
    public string Iniciales
    {
        get
        {
            var partes = SesionActual.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return partes.Length switch
            {
                0 => "?",
                1 => partes[0][..1].ToUpperInvariant(),
                _ => $"{partes[0][..1]}{partes[^1][..1]}".ToUpperInvariant()
            };
        }
    }

    /// <summary>Llamar tras el login: refresca permisos y abre la página inicial.</summary>
    public void Inicializar()
    {
        OnPropertyChanged(string.Empty);   // reevalúa todos los PuedeVer* y datos de usuario
        Navegar(PaginaInicial());
    }

    /// <summary>Primera página visible según el rol (el Cajero no ve Panel).</summary>
    private static Pagina PaginaInicial() =>
        SesionActual.TienePermiso("panel") ? Pagina.Panel : Pagina.Vender;

    [RelayCommand]
    private void Navegar(Pagina destino)
    {
        // Defensa en profundidad: el sidebar ya filtra, pero la navegación
        // interna (p. ej. atajos futuros) también debe validar permisos
        if (!SesionActual.TienePermiso(PermisoPorPagina[destino]))
        {
            _dialogos.MostrarError("Sin permiso",
                "No tienes permisos para acceder a este módulo.");
            return;
        }

        PaginaActual = destino;
        PaginaActualVm = _paginas[destino];
        TituloPagina = Titulos[destino];

        // Las páginas de datos recargan al entrar (fire-and-forget: la UI
        // no se bloquea y el VM maneja sus propios errores)
        if (PaginaActualVm is IPaginaAsincrona pagina)
            _ = pagina.RefrescarAsync();
    }

    /// <summary>Sustituye el placeholder por el ViewModel real (usado por fases futuras).</summary>
    public void RegistrarPagina(Pagina pagina, object viewModel) =>
        _paginas[pagina] = viewModel;

    /// <summary>
    /// Muestra un VM que no es página del sidebar (formularios nuevo/editar)
    /// manteniendo iluminada la sección a la que pertenece.
    /// </summary>
    public void MostrarSubpagina(Pagina seccion, object viewModel, string titulo)
    {
        PaginaActual = seccion;
        PaginaActualVm = viewModel;
        TituloPagina = titulo;
    }

    /// <summary>Vuelve a la página del sidebar (al guardar/cancelar un formulario).</summary>
    public void VolverA(Pagina pagina) => Navegar(pagina);
}
