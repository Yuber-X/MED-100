namespace MED100.Common;

/// <summary>
/// Cómo se llama el producto y en qué versión va.
///
/// EL NOMBRE VISIBLE ES "MediControl" (decidido el 2026-09-06). "MED-100" era
/// el nombre provisional del proyecto y sigue vivo por debajo a propósito:
///
///   · <c>AppId</c> del instalador — cambiarlo haría que Windows viera una
///     aplicación DISTINTA, y el cliente terminaría con dos instaladas.
///   · <c>%ProgramData%\MED-100\licencia.dat</c> — es el ancla de la licencia.
///     Moverla reiniciaría el demo de 15 días en cada equipo ya activado.
///   · Los namespaces <c>MED100.*</c> y la base <c>med100_db</c> — renombrarlos
///     es caro y no se ve.
///
/// O sea: MED-100 es el nombre interno del proyecto, MediControl es el nombre
/// del producto. Lo que se muestra sale SIEMPRE de acá.
///
/// <see cref="Version"/> tiene que coincidir con la del instalador
/// (installer/MED100.iss) y con la del .csproj. El actualizador compara la
/// versión del .exe contra la que trae, y si no coinciden avisa que la
/// actualización no entró.
/// </summary>
public static class AppInfo
{
    /// <summary>El nombre que ve el cliente. Único lugar donde se escribe.</summary>
    public const string Nombre = "MediControl";

    /// <summary>Versión del producto. Debe coincidir con el .csproj y el .iss.</summary>
    public const string Version = "1.2.0";

    /// <summary>"MediControl 1.2.0" — para encabezados y la ventana Acerca de.</summary>
    public static string NombreYVersion => $"{Nombre} {Version}";

    /// <summary>"Versión 1.2.0" — lo que se muestra abajo en la pantalla de inicio.</summary>
    public static string EtiquetaVersion => $"Versión {Version}";
}
