using System.Text.RegularExpressions;
using MySqlConnector;

namespace MED100.Data;

/// <summary>Resultado del diagnóstico de conexión al arrancar la app.</summary>
public enum EstadoBaseDatos
{
    /// <summary>Conecta y el esquema existe: se puede operar.</summary>
    Lista,

    /// <summary>El servidor responde pero la base de datos (o sus tablas) no existe.</summary>
    FaltaBaseDatos,

    /// <summary>Usuario/contraseña de la cadena de conexión rechazados.</summary>
    CredencialesInvalidas,

    /// <summary>MySQL no responde (servicio detenido, puerto o host incorrectos).</summary>
    SinServidor
}

/// <summary>
/// Diagnostica el estado de la base de datos al arrancar y permite crear el
/// esquema completo (patrón v1.0.1 de PrestControl). Diferencias MED-100:
///  - también ejecuta el seed de roles/permisos (002), obligatorio para operar;
///  - el esquema tiene TRIGGERS: los bloques DELIMITER $$ ... $$ se ejecutan
///    como comandos individuales (el protocolo no acepta DELIMITER).
/// Ambos scripts viajan embebidos: única fuente de verdad con scripts/db/.
/// </summary>
public class VerificadorBaseDatos
{
    private readonly string _cadenaConexion;

    public VerificadorBaseDatos(ConexionFactory fabrica) => _cadenaConexion = fabrica.CadenaConexion;

    /// <summary>Permite inyectar la cadena directamente (tests de integración).</summary>
    public VerificadorBaseDatos(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    public async Task<EstadoBaseDatos> VerificarAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conexion = new MySqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);

            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = "SHOW TABLES LIKE 'usuario';";
            var tabla = await cmd.ExecuteScalarAsync(ct);
            return tabla is null ? EstadoBaseDatos.FaltaBaseDatos : EstadoBaseDatos.Lista;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.UnknownDatabase)
        {
            return EstadoBaseDatos.FaltaBaseDatos;
        }
        catch (MySqlException ex) when (
            ex.ErrorCode == MySqlErrorCode.AccessDenied ||
            ex.ErrorCode == MySqlErrorCode.DatabaseAccessDenied)
        {
            return EstadoBaseDatos.CredencialesInvalidas;
        }
        catch (MySqlException)
        {
            return EstadoBaseDatos.SinServidor;
        }
    }

    /// <summary>
    /// Crea la base de datos (nombre tomado de la cadena de conexión), ejecuta el
    /// esquema (con triggers) y el seed de roles/permisos. Requiere un usuario
    /// MySQL con permisos CREATE y TRIGGER (root sí; el dedicado no, por diseño).
    /// </summary>
    public async Task CrearEsquemaAsync(CancellationToken ct = default)
    {
        var constructor = new MySqlConnectionStringBuilder(_cadenaConexion);
        var nombreBd = constructor.Database;
        if (string.IsNullOrEmpty(nombreBd) || !Regex.IsMatch(nombreBd, @"^[0-9A-Za-z$_]+$"))
            throw new InvalidOperationException(
                $"Nombre de base de datos no válido en la cadena de conexión: '{nombreBd}'.");

        constructor.Database = string.Empty;
        await using var conexion = new MySqlConnection(constructor.ConnectionString);
        await conexion.OpenAsync(ct);

        await using (var crear = conexion.CreateCommand())
        {
            // El nombre no puede parametrizarse en DDL; viene del App.config
            // local (no de entrada del usuario) y ya fue validado arriba.
            crear.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{nombreBd}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await crear.ExecuteNonQueryAsync(ct);
        }

        await conexion.ChangeDatabaseAsync(nombreBd, ct);

        foreach (var bloque in ObtenerBloquesEjecutables())
        {
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = bloque;
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Pone al día una base que ya existía. Hoy solo crea la tabla
    /// <c>licencia</c> (migración 008): sin ella, una instalación que venía de
    /// una versión anterior no arrancaría, porque la licencia se consulta antes
    /// del login.
    ///
    /// No es un migrador de verdad —no hay tabla de versiones ni orden de
    /// scripts—: es el mínimo para que actualizar la app no obligue a nadie a
    /// abrir MySQL Workbench. Las migraciones 005, 006 y 007 siguen siendo
    /// manuales; si algún día son tres o cuatro más, esto pide un migrador
    /// como el de FAControl.
    /// </summary>
    /// <returns>null si quedó al día; el motivo si algo no se pudo aplicar.</returns>
    public async Task<string?> ActualizarEsquemaAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conexion = new MySqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);

            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS licencia (
                  id                  TINYINT UNSIGNED NOT NULL,
                  instalada_at_utc    DATETIME     NOT NULL,
                  activada            TINYINT(1)   NOT NULL DEFAULT 0,
                  activada_at_utc     DATETIME     NULL,
                  activada_por        VARCHAR(80)  NULL,
                  ultima_apertura_utc DATETIME     NOT NULL,
                  PRIMARY KEY (id),
                  CONSTRAINT ck_licencia_fila_unica CHECK (id = 1)
                ) ENGINE=InnoDB;
                """;
            await cmd.ExecuteNonQueryAsync(ct);
            return null;
        }
        catch (MySqlException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Prepara los scripts embebidos para ejecución por protocolo:
    ///  - retira CREATE DATABASE y USE (la BD la decide la cadena de conexión);
    ///  - la parte normal va en un solo batch multi-statement;
    ///  - cada trigger (entre DELIMITER $$ y DELIMITER ;) va como comando aparte.
    /// </summary>
    internal static List<string> ObtenerBloquesEjecutables()
    {
        var bloques = new List<string>();
        foreach (var recurso in new[] { "MED100.Data.001_create_schema.sql", "MED100.Data.002_seed_data.sql" })
        {
            var sql = LeerRecurso(recurso);
            sql = Regex.Replace(sql, @"CREATE DATABASE[^;]+;", string.Empty, RegexOptions.IgnoreCase);
            sql = Regex.Replace(sql, @"^\s*USE\s+[0-9A-Za-z$_]+\s*;", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Separar la zona de triggers: DELIMITER $$ ... DELIMITER ;
            var partes = Regex.Split(sql, @"^\s*DELIMITER\s+\$\$\s*$", RegexOptions.Multiline);
            AgregarSiTieneContenido(bloques, partes[0]);

            for (var i = 1; i < partes.Length; i++)
            {
                var zona = Regex.Split(partes[i], @"^\s*DELIMITER\s+;\s*$", RegexOptions.Multiline);
                foreach (var trigger in zona[0].Split("$$", StringSplitOptions.RemoveEmptyEntries))
                    AgregarSiTieneContenido(bloques, trigger);
                if (zona.Length > 1)
                    AgregarSiTieneContenido(bloques, zona[1]);
            }
        }
        return bloques;
    }

    private static void AgregarSiTieneContenido(List<string> bloques, string sql)
    {
        // Un bloque de solo comentarios/espacios no es ejecutable
        var sinComentarios = Regex.Replace(sql, @"^\s*--.*$", string.Empty, RegexOptions.Multiline);
        if (!string.IsNullOrWhiteSpace(sinComentarios))
            bloques.Add(sql.Trim());
    }

    private static string LeerRecurso(string nombre)
    {
        var ensamblado = typeof(VerificadorBaseDatos).Assembly;
        using var flujo = ensamblado.GetManifestResourceStream(nombre)
            ?? throw new InvalidOperationException($"Recurso embebido '{nombre}' no encontrado.");
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }
}
