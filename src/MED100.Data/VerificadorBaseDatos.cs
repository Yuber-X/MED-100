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
    /// Un parche de apertura. <see cref="SoloSiFaltaColumna"/> lo saltea cuando
    /// la columna ya está: es la forma de que un ALTER sea idempotente sin
    /// <c>ADD COLUMN IF NOT EXISTS</c>, que MySQL 8 no tiene.
    ///
    /// La pregunta se hace desde C# y NO con <c>SET @existe := ...</c> en SQL:
    /// las variables de usuario necesitan <c>Allow User Variables=true</c> en
    /// la cadena de conexión, y la de la app no lo trae. Los scripts sueltos de
    /// <c>scripts/db/</c> sí las usan porque corren por Workbench, donde el
    /// driver no se mete.
    /// </summary>
    private sealed record Parche(string Motivo, string Sql)
    {
        public (string Tabla, string Columna)? SoloSiFaltaColumna { get; init; }
    }

    /// <summary>
    /// Los parches que se aplican al abrir sobre una base que ya existía.
    ///
    /// Cada uno tiene que ser IDEMPOTENTE: la app los corre en cada arranque y
    /// el segundo no puede fallar. MySQL 8 no tiene <c>ADD COLUMN IF NOT
    /// EXISTS</c>, así que las altas de columna preguntan primero a
    /// information_schema.
    ///
    /// No es un migrador de verdad —no hay tabla de versiones ni orden—: es el
    /// mínimo para que actualizar la app no obligue a nadie a abrir Workbench.
    /// Las migraciones 005, 006 y 007 siguen siendo manuales. Cuando esta lista
    /// pase de media docena, conviene traer el migrador de FAControl.
    /// </summary>
    private static readonly Parche[] ParchesDeApertura =
    [
        // 008 — sin la tabla licencia la app no arranca: se consulta ANTES del login.
        new("crear la tabla licencia", """
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
            """),

        // 009 — la ficha del paciente lee esta columna; sin ella, revienta.
        new("agregar cliente.ultima_visita_previa",
            "ALTER TABLE cliente ADD COLUMN ultima_visita_previa DATE NULL AFTER referidor_id;")
            { SoloSiFaltaColumna = ("cliente", "ultima_visita_previa") },

        // 010 — el ticket lee precio_catalogo para poder mostrar la rebaja.
        new("agregar detalle.precio_catalogo",
            "ALTER TABLE detalle ADD COLUMN precio_catalogo DECIMAL(15,2) NULL AFTER precio_unitario;")
            { SoloSiFaltaColumna = ("detalle", "precio_catalogo") },

        // 010 — el permiso de rebajar precios, para Admin y Supervisor. Los
        // usuarios ya creados tienen sus permisos COPIADOS en usuario_permiso
        // por el trigger del alta, así que darlo solo al rol no los alcanza.
        //
        // No se filtra por `activo`: un usuario dado de baja no puede entrar de
        // todos modos, y dejarlo fuera haría que le faltara el permiso el día
        // que lo reactiven — un bug que aparecería meses después.
        new("dar de alta el permiso precio_editar", """
            INSERT INTO permiso (codigo, nombre, descripcion)
            SELECT 'precio_editar', 'Rebajar precios al cobrar',
                   'Cambiar el precio de una línea en la pantalla de cobro'
            WHERE NOT EXISTS (SELECT 1 FROM permiso WHERE codigo = 'precio_editar');

            INSERT INTO rol_permiso (rol_id, permiso_id)
            SELECT r.id, p.id FROM rol r
              JOIN permiso p ON p.codigo = 'precio_editar'
             WHERE r.nombre IN ('Admin', 'Supervisor')
               AND NOT EXISTS (SELECT 1 FROM rol_permiso rp
                                WHERE rp.rol_id = r.id AND rp.permiso_id = p.id);

            INSERT INTO usuario_permiso (usuario_id, permiso_id)
            SELECT u.id, p.id FROM usuario u
              JOIN rol_permiso rp ON rp.rol_id = u.rol_id
              JOIN permiso p ON p.id = rp.permiso_id AND p.codigo = 'precio_editar'
             WHERE NOT EXISTS (SELECT 1 FROM usuario_permiso up
                                WHERE up.usuario_id = u.id AND up.permiso_id = p.id);
            """),
    ];

    /// <summary>
    /// Si la columna ya existe. Consulta parametrizada: el nombre de la tabla
    /// nunca se concatena, aunque hoy venga de una constante del código.
    /// </summary>
    private static async Task<bool> ExisteColumnaAsync(MySqlConnection conexion,
        string tabla, string columna, CancellationToken ct)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE()
               AND table_name   = @tabla
               AND column_name  = @columna;
            """;
        cmd.Parameters.AddWithValue("@tabla", tabla);
        cmd.Parameters.AddWithValue("@columna", columna);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>
    /// Pone al día una base que ya existía aplicando <see cref="ParchesDeApertura"/>.
    /// </summary>
    /// <returns>null si quedó al día; el motivo si algo no se pudo aplicar.</returns>
    public async Task<string?> ActualizarEsquemaAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conexion = new MySqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);

            foreach (var parche in ParchesDeApertura)
            {
                if (parche.SoloSiFaltaColumna is { } destino &&
                    await ExisteColumnaAsync(conexion, destino.Tabla, destino.Columna, ct))
                {
                    continue;
                }

                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = parche.Sql;
                cmd.CommandTimeout = 120;
                try
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (MySqlException ex)
                {
                    // Se nombra CUÁL falló: "Unknown column" a secas, sobre una
                    // lista de parches, no dice nada útil a las 8 de la mañana.
                    return $"No se pudo {parche.Motivo}: {ex.Message}";
                }
            }

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
