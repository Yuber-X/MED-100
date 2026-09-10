namespace MED100.Common;

/// <summary>
/// Cómo se llama el producto y en qué versión va.
///
/// EL NOMBRE VISIBLE ES "Odonto Unión" — se lo puso la clínica el 2026-09-07
/// ("ya te mando el nombre: Odonto unión"). Antes fue MediControl, y antes
/// MED-100. Lo de abajo NO cambia con el nombre, a propósito:
///
///   · <c>AppId</c> del instalador — cambiarlo haría que Windows viera una
///     aplicación DISTINTA, y el cliente terminaría con dos instaladas.
///   · <c>%ProgramData%\MED-100\licencia.dat</c> — es el ancla de la licencia.
///     Moverla reiniciaría el demo de 15 días en cada equipo ya activado.
///   · Los namespaces <c>MED100.*</c> y la base <c>med100_db</c> — renombrarlos
///     es caro y no se ve.
///   · El MUTEX <c>Global\MediControl.App.Instancia</c> — es el que mira el
///     actualizador para negarse a correr con la aplicación abierta. Si se
///     renombra, el actualizador nuevo busca un mutex que la versión YA
///     INSTALADA no crea, no la detecta, y actualiza con el programa abierto:
///     exactamente el fallo que costó el fin de semana del 2026-09-05 en
///     FAControl. El nombre del mutex es un identificador, no una marca.
///
/// O sea: MED-100 es el nombre interno del proyecto y Odonto Unión es el
/// nombre del producto. Lo que se muestra sale SIEMPRE de acá.
///
/// <see cref="Version"/> tiene que coincidir con la del instalador
/// (installer/MED100.iss) y con la del .csproj. El actualizador compara la
/// versión del .exe contra la que trae, y si no coinciden avisa que la
/// actualización no entró.
/// </summary>
public static class AppInfo
{
    /// <summary>El nombre que ve el cliente. Único lugar donde se escribe.</summary>
    public const string Nombre = "Odonto Unión";

    /// <summary>Versión del producto. Debe coincidir con el .csproj y el .iss.</summary>
    public const string Version = "1.3.0";

    /// <summary>"Odonto Unión 1.3.0" — para encabezados y la ventana Acerca de.</summary>
    public static string NombreYVersion => $"{Nombre} {Version}";

    /// <summary>"Versión 1.3.0" — lo que se muestra abajo en la pantalla de inicio.</summary>
    public static string EtiquetaVersion => $"Versión {Version}";
}
