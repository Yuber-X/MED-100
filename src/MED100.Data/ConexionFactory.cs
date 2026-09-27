using System.Configuration;
using MySqlConnector;

namespace MED100.Data;

/// <summary>
/// Adaptación del patrón CConexion del POS-400 a WPF/.NET 8:
///  - cadena de conexión leída de App.config (nunca hardcodeada)
///  - conexiones async y desechables (using) — sin estado compartido
///  - los errores se propagan al llamador (Serilog los registra arriba);
///    aquí NUNCA se muestra UI (nada de MessageBox en capa de datos)
/// </summary>
public class ConexionFactory
{
    private readonly string _cadenaConexion;

    /// <summary>
    /// Variable de entorno que pisa la cadena del App.config.
    ///
    /// Existe para la máquina del desarrollador: el App.config del repositorio
    /// viaja con los valores de ejemplo (CAMBIAR_USUARIO / CAMBIAR_PASSWORD)
    /// para que las credenciales NUNCA se suban al repositorio, y sin esto la
    /// única forma de correr desde el código es editar un archivo versionado y
    /// acordarse de no subirlo. En la máquina del cliente no se usa: ahí la
    /// cadena la escribe el instalador en MED100.App.dll.config.
    /// </summary>
    public const string VariableDeEntorno = "MED100_CONEXION";

    /// <summary>
    /// Lee la cadena de conexión: primero la variable de entorno
    /// <see cref="VariableDeEntorno"/>, si no, la cadena "MED100Db" del
    /// App.config.
    /// </summary>
    public ConexionFactory()
    {
        var delEntorno = Environment.GetEnvironmentVariable(VariableDeEntorno);
        if (!string.IsNullOrWhiteSpace(delEntorno))
        {
            _cadenaConexion = delEntorno.Trim();
            return;
        }

        var config = ConfigurationManager.ConnectionStrings["MED100Db"]
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión 'MED100Db' en App.config.");
        _cadenaConexion = config.ConnectionString;
    }

    /// <summary>Permite inyectar la cadena directamente (tests de integración).</summary>
    public ConexionFactory(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    /// <summary>Expuesta para servicios que invocan herramientas externas (mysqldump).</summary>
    public string CadenaConexion => _cadenaConexion;

    /// <summary>
    /// True si la cadena todavía tiene los valores de ejemplo del repositorio.
    /// MySQL los rechaza como cualquier credencial mala, y el mensaje genérico
    /// ("revisá la cadena de conexión") manda a mirar un archivo donde no se ve
    /// nada raro. Con esto el aviso puede decir lo que realmente pasa.
    /// </summary>
    public bool EsCadenaDeEjemplo => _cadenaConexion.Contains("CAMBIAR_", StringComparison.Ordinal);

    /// <summary>Abre una conexión nueva. El llamador es dueño de su ciclo de vida (using).</summary>
    public async Task<MySqlConnection> AbrirAsync(CancellationToken ct = default)
    {
        var conexion = new MySqlConnection(_cadenaConexion);
        await conexion.OpenAsync(ct);
        return conexion;
    }
}
