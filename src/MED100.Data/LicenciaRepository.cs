using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// La fila única (id = 1) de <c>licencia</c>: cuándo se instaló, si se activó
/// y cuándo se abrió la app por última vez.
///
/// La fila se crea sola la primera vez que se consulta, igual que hace el
/// resto de la app con la configuración: no depende de que alguien se acuerde
/// de ejecutar un INSERT en la instalación.
/// </summary>
public class LicenciaRepository
{
    private readonly ConexionFactory _factory;

    public LicenciaRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Lee la licencia, creando la fila si es la primera vez. <paramref name="ahoraUtc"/>
    /// se pasa desde afuera para que los tests puedan situarse en cualquier día.
    /// </summary>
    public async Task<Licencia> ObtenerAsync(DateTime ahoraUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);

        using (var crear = conexion.CreateCommand())
        {
            // INSERT IGNORE y no "SELECT y si no hay, INSERT": con dos
            // terminales abriendo a la vez, el segundo camino inserta dos veces.
            crear.CommandText = $"""
                INSERT IGNORE INTO {DbNames.Licencia}
                    (id, instalada_at_utc, activada, ultima_apertura_utc)
                VALUES (1, @ahora, 0, @ahora);
                """;
            crear.Parameters.AddWithValue("@ahora", ahoraUtc);
            await crear.ExecuteNonQueryAsync(ct);
        }

        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT instalada_at_utc, activada, activada_at_utc, activada_por, ultima_apertura_utc
            FROM {DbNames.Licencia}
            WHERE id = 1;
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException(
                "La tabla licencia está vacía y no se pudo crear su fila inicial.");

        return new Licencia
        {
            InstaladaAtUtc = DateTime.SpecifyKind(reader.GetDateTime("instalada_at_utc"), DateTimeKind.Utc),
            Activada = reader.GetBoolean("activada"),
            ActivadaAtUtc = reader.IsDBNull(reader.GetOrdinal("activada_at_utc"))
                ? null
                : DateTime.SpecifyKind(reader.GetDateTime("activada_at_utc"), DateTimeKind.Utc),
            ActivadaPor = reader.IsDBNull(reader.GetOrdinal("activada_por"))
                ? null
                : reader.GetString("activada_por"),
            UltimaAperturaUtc = DateTime.SpecifyKind(reader.GetDateTime("ultima_apertura_utc"), DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// Deja constancia de este arranque. GREATEST y no una asignación directa:
    /// si el reloj está atrasado, escribir la hora de ahora borraría justamente
    /// la prueba de que se movió.
    /// </summary>
    public async Task RegistrarAperturaAsync(DateTime ahoraUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Licencia}
            SET ultima_apertura_utc = GREATEST(ultima_apertura_utc, @ahora)
            WHERE id = 1;
            """;
        cmd.Parameters.AddWithValue("@ahora", ahoraUtc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Atrasa la fecha de instalación cuando el ancla del disco tiene una más
    /// vieja que la base — o sea, cuando alguien borró la base para volver a
    /// empezar el demo. Solo hacia atrás: nunca regala días.
    /// </summary>
    public async Task RetrasarInicioAsync(DateTime inicioUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Licencia}
            SET instalada_at_utc = @inicio
            WHERE id = 1 AND instalada_at_utc > @inicio;
            """;
        cmd.Parameters.AddWithValue("@inicio", inicioUtc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Marca la instalación como activada. Idempotente: activar dos veces no
    /// pisa la fecha ni el nombre de quien lo hizo la primera vez.
    /// </summary>
    public async Task ActivarAsync(string? usuario, DateTime ahoraUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Licencia}
            SET activada = 1, activada_at_utc = @ahora, activada_por = @usuario
            WHERE id = 1 AND activada = 0;
            """;
        cmd.Parameters.AddWithValue("@ahora", ahoraUtc);
        cmd.Parameters.AddWithValue("@usuario", (object?)usuario ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
