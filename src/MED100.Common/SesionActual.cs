namespace MED100.Common;

/// <summary>
/// Sesión del usuario autenticado (patrón del POS-400, ahora multiusuario:
/// incluye rol y permisos efectivos). Se establece en el login y se limpia
/// en el logout. NUNCA cachear estos valores en variables que sobrevivan
/// al logout — otro empleado puede iniciar sesión después.
/// </summary>
public static class SesionActual
{
    public static long Id { get; private set; }
    public static string Username { get; private set; } = string.Empty;
    public static string Nombre { get; private set; } = string.Empty;
    public static string Rol { get; private set; } = string.Empty;
    public static DateTime LoginAtUtc { get; private set; }
    public static long SesionId { get; private set; }

    private static HashSet<string> _permisos = [];

    public static bool HaySesionActiva => Id > 0;
    public static bool EsAdmin => Rol == "Admin";

    /// <summary>True si el usuario tiene el permiso (código de la tabla permiso).</summary>
    public static bool TienePermiso(string codigoPermiso) => _permisos.Contains(codigoPermiso);

    public static void Iniciar(long id, string username, string nombre, string rol,
        IEnumerable<string> permisos, DateTime loginAtUtc, long sesionId)
    {
        Id = id;
        Username = username;
        Nombre = nombre;
        Rol = rol;
        _permisos = [.. permisos];
        LoginAtUtc = loginAtUtc;
        SesionId = sesionId;
    }

    public static void Cerrar()
    {
        Id = 0;
        Username = string.Empty;
        Nombre = string.Empty;
        Rol = string.Empty;
        _permisos = [];
        LoginAtUtc = default;
        SesionId = 0;
    }
}
