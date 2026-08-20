using System.Diagnostics;
using System.IO;
using MySqlConnector;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Respaldo y restauración de la base de datos con las herramientas oficiales
/// de MySQL (mysqldump / mysql). Portado de PrestControl.
///
/// Es el camino recomendado para MIGRAR DE EQUIPO: el .sql conserva ids,
/// relaciones y numeración de facturas exactos. (El Excel es para consulta,
/// no para migrar.)
///
/// La contraseña viaja por la variable de entorno MYSQL_PWD — nunca por la
/// línea de comandos, donde sería visible en el administrador de tareas.
/// </summary>
public class RespaldoService
{
    private readonly string _servidor;
    private readonly uint _puerto;
    private readonly string _usuario;
    private readonly string _password;
    private readonly string _baseDatos;

    public RespaldoService(string cadenaConexion)
    {
        var builder = new MySqlConnectionStringBuilder(cadenaConexion);
        _servidor = builder.Server;
        _puerto = builder.Port;
        _usuario = builder.UserID;
        _password = builder.Password;
        _baseDatos = builder.Database;
    }

    /// <summary>Genera el respaldo .sql completo en la ruta indicada.</summary>
    public async Task RespaldarAsync(string rutaDestino, CancellationToken ct = default)
    {
        var mysqldump = BuscarHerramienta("mysqldump.exe");
        var info = CrearProceso(mysqldump,
            $"--host={_servidor} --port={_puerto} --user={_usuario} " +
            $"--single-transaction --routines --triggers --add-drop-table {_baseDatos}");

        using var proceso = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar mysqldump.");

        var salida = await proceso.StandardOutput.ReadToEndAsync(ct);
        var errores = await proceso.StandardError.ReadToEndAsync(ct);
        await proceso.WaitForExitAsync(ct);

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"mysqldump falló (código {proceso.ExitCode}): {errores}");

        await File.WriteAllTextAsync(rutaDestino, salida, ct);
        Log.Information("Respaldo generado en {Ruta} ({Bytes} bytes)", rutaDestino, salida.Length);
    }

    /// <summary>
    /// Restaura la BD desde un archivo .sql. DESTRUCTIVO: reemplaza los datos
    /// actuales — quien llama DEBE confirmar dos veces y sugerir respaldo previo.
    /// </summary>
    public async Task RestaurarAsync(string rutaArchivo, CancellationToken ct = default)
    {
        if (!File.Exists(rutaArchivo))
            throw new FileNotFoundException("No se encontró el archivo de respaldo.", rutaArchivo);

        var mysql = BuscarHerramienta("mysql.exe");
        var info = CrearProceso(mysql,
            $"--host={_servidor} --port={_puerto} --user={_usuario} {_baseDatos}");
        info.RedirectStandardInput = true;

        using var proceso = Process.Start(info)
            ?? throw new InvalidOperationException("No se pudo iniciar mysql.");

        using (var lector = File.OpenText(rutaArchivo))
        {
            string? linea;
            while ((linea = await lector.ReadLineAsync(ct)) is not null)
                await proceso.StandardInput.WriteLineAsync(linea);
        }
        proceso.StandardInput.Close();

        var errores = await proceso.StandardError.ReadToEndAsync(ct);
        await proceso.WaitForExitAsync(ct);

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"mysql falló (código {proceso.ExitCode}): {errores}");

        Log.Information("Base de datos restaurada desde {Ruta}", rutaArchivo);
    }

    private ProcessStartInfo CrearProceso(string ejecutable, string argumentos)
    {
        var info = new ProcessStartInfo(ejecutable, argumentos)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        info.EnvironmentVariables["MYSQL_PWD"] = _password;
        return info;
    }

    /// <summary>Busca la herramienta en el PATH y en las rutas típicas de instalación.</summary>
    public static string BuscarHerramienta(string nombreExe)
    {
        var rutas = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';');
        foreach (var ruta in rutas)
        {
            var candidato = Path.Combine(ruta.Trim(), nombreExe);
            if (File.Exists(candidato))
                return candidato;
        }

        foreach (var programas in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            var baseMySql = Path.Combine(programas, "MySQL");
            if (!Directory.Exists(baseMySql))
                continue;
            foreach (var carpeta in Directory.GetDirectories(baseMySql, "MySQL Server*")
                         .OrderByDescending(c => c))
            {
                var candidato = Path.Combine(carpeta, "bin", nombreExe);
                if (File.Exists(candidato))
                    return candidato;
            }
        }

        throw new FileNotFoundException(
            $"No se encontró {nombreExe}. Verifica que MySQL Server esté instalado " +
            "o agrega su carpeta bin al PATH.");
    }
}
