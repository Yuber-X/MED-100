using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Secuencia de comprobantes fiscales (011_ncf_secuencia.sql).
///
/// La reserva del siguiente NCF es atómica (<c>SELECT … FOR UPDATE</c> DENTRO de
/// la transacción de la factura): dos cajeros cobrando a la vez jamás reciben el
/// mismo comprobante, y si la venta hace rollback el número no se consume.
///
/// Es la misma garantía que ya rige <c>configuracion_negocio.factura_siguiente</c>,
/// y por la misma razón: un número repetido en el libro de ventas no se arregla
/// desde la app, se arregla con el contador.
/// </summary>
public class NcfRepository
{
    private readonly ConexionFactory _factory;

    public NcfRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>La secuencia activa, o null si la clínica no configuró ninguna.</summary>
    public async Task<NcfSecuencia?> ObtenerActivaAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
            FROM {DbNames.NcfSecuencia}
            WHERE activo = 1
            ORDER BY id
            LIMIT 1;
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>Crea o actualiza la secuencia (upsert por prefijo).</summary>
    public async Task GuardarAsync(NcfSecuencia secuencia, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.NcfSecuencia}
              (prefijo, largo, proxima, fin_rango, vencimiento, activo)
            VALUES
              (@prefijo, @largo, @proxima, @fin, @vencimiento, @activo)
            ON DUPLICATE KEY UPDATE
              largo = @largo, proxima = @proxima, fin_rango = @fin,
              vencimiento = @vencimiento, activo = @activo, updated_at = UTC_TIMESTAMP();
            """;
        cmd.Parameters.AddWithValue("@prefijo", secuencia.Prefijo);
        cmd.Parameters.AddWithValue("@largo", secuencia.Largo);
        cmd.Parameters.AddWithValue("@proxima", secuencia.Proxima);
        cmd.Parameters.AddWithValue("@fin", (object?)secuencia.FinRango ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vencimiento",
            (object?)secuencia.Vencimiento?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", secuencia.Activo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Apaga la numeración local: la clínica vuelve a pegar a mano el NCF que le
    /// genere el Facturador Gratuito de la DGII.
    /// </summary>
    public async Task DesactivarTodasAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            $"UPDATE {DbNames.NcfSecuencia} SET activo = 0, updated_at = UTC_TIMESTAMP() WHERE activo = 1;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Adopta como PREDETERMINADO el comprobante que se acaba de pegar a mano,
    /// para que la secuencia siga a partir de él.
    ///
    /// Devuelve true si movió algo. No hace nada —y no falla— cuando:
    ///  * el texto no tiene forma de comprobante de la DGII, o
    ///  * es de la MISMA serie y su número ya quedó atrás.
    ///
    /// Ese segundo caso es deliberado: retroceder dentro de la misma serie
    /// volvería a entregar números ya consumidos, que la DGII prohíbe reusar y
    /// que <c>uq_factura_ncf</c> rechazaría más tarde con un error de base de
    /// datos en la cara del cajero. Si la serie CAMBIA sí se adopta tal cual: un
    /// talonario nuevo trae su propia numeración y no pisa nada.
    /// </summary>
    public async Task<bool> AdoptarComoPredeterminadaAsync(string? ncfUsado,
        CancellationToken ct = default)
    {
        if (NcfSecuencia.Descomponer(ncfUsado) is not { } partes)
            return false;
        var (prefijo, numero, largo) = partes;
        var siguiente = numero + 1;

        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            NcfSecuencia? activa = null;
            using (var select = conexion.CreateCommand())
            {
                select.Transaction = transaccion;
                select.CommandText = $"""
                    SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
                    FROM {DbNames.NcfSecuencia}
                    WHERE activo = 1
                    ORDER BY id
                    LIMIT 1
                    FOR UPDATE;
                    """;
                using var reader = await select.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    activa = Mapear(reader);
            }

            if (activa is not null && activa.Prefijo == prefijo && siguiente <= activa.Proxima)
                return false;

            // Serie distinta: la anterior se apaga para que ObtenerActivaAsync no
            // siga devolviéndola (toma la PRIMERA activa por id, no la última).
            if (activa is null || activa.Prefijo != prefijo)
            {
                using var apagar = conexion.CreateCommand();
                apagar.Transaction = transaccion;
                apagar.CommandText = $"UPDATE {DbNames.NcfSecuencia} " +
                    "SET activo = 0, updated_at = UTC_TIMESTAMP() WHERE activo = 1;";
                await apagar.ExecuteNonQueryAsync(ct);
            }

            using (var guardar = conexion.CreateCommand())
            {
                guardar.Transaction = transaccion;
                // El rango y el vencimiento NO se heredan: una serie nueva queda
                // sin tope hasta que el Admin cargue la autorización de la DGII.
                guardar.CommandText = $"""
                    INSERT INTO {DbNames.NcfSecuencia}
                      (prefijo, largo, proxima, fin_rango, vencimiento, activo)
                    VALUES
                      (@prefijo, @largo, @proxima, NULL, NULL, 1)
                    ON DUPLICATE KEY UPDATE
                      largo = @largo, proxima = @proxima, activo = 1,
                      updated_at = UTC_TIMESTAMP();
                    """;
                guardar.Parameters.AddWithValue("@prefijo", prefijo);
                guardar.Parameters.AddWithValue("@largo", largo);
                guardar.Parameters.AddWithValue("@proxima", siguiente);
                await guardar.ExecuteNonQueryAsync(ct);
            }

            await transaccion.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Reserva el siguiente NCF DENTRO de la transacción de la factura.
    /// Tira si no hay secuencia activa, está vencida o agotada.
    /// </summary>
    public static async Task<string> ReservarSiguienteAsync(MySqlConnection conexion,
        MySqlTransaction transaccion, DateOnly hoy, CancellationToken ct = default)
    {
        NcfSecuencia secuencia;
        using (var select = conexion.CreateCommand())
        {
            select.Transaction = transaccion;
            select.CommandText = $"""
                SELECT id, prefijo, largo, proxima, fin_rango, vencimiento, activo
                FROM {DbNames.NcfSecuencia}
                WHERE activo = 1
                ORDER BY id
                LIMIT 1
                FOR UPDATE;
                """;
            using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException(
                    "No hay una secuencia de comprobantes fiscales configurada. " +
                    "Cargala en Configuración → Comprobante fiscal, o escribí el NCF a mano.");
            secuencia = Mapear(reader);
        }

        if (secuencia.EstaVencida(hoy))
            throw new InvalidOperationException(
                $"La secuencia de comprobantes {secuencia.Prefijo} venció el {secuencia.Vencimiento:dd/MM/yyyy}. " +
                "Solicitá una nueva autorización a la DGII y actualizá la configuración.");
        if (secuencia.EstaAgotada)
            throw new InvalidOperationException(
                $"La secuencia de comprobantes {secuencia.Prefijo} se agotó (fin del rango autorizado). " +
                "Solicitá una nueva autorización a la DGII y actualizá la configuración.");

        var ncf = secuencia.Formatear(secuencia.Proxima);

        using (var update = conexion.CreateCommand())
        {
            update.Transaction = transaccion;
            update.CommandText = $"""
                UPDATE {DbNames.NcfSecuencia}
                SET proxima = proxima + 1, updated_at = UTC_TIMESTAMP()
                WHERE id = @id;
                """;
            update.Parameters.AddWithValue("@id", secuencia.Id);
            await update.ExecuteNonQueryAsync(ct);
        }

        return ncf;
    }

    private static NcfSecuencia Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt32("id"),
        Prefijo = reader.GetString("prefijo"),
        Largo = reader.GetInt32("largo"),
        Proxima = reader.GetInt64("proxima"),
        FinRango = reader.IsDBNull(reader.GetOrdinal("fin_rango")) ? null : reader.GetInt64("fin_rango"),
        Vencimiento = reader.IsDBNull(reader.GetOrdinal("vencimiento"))
            ? null
            : DateOnly.FromDateTime(reader.GetDateTime("vencimiento")),
        Activo = reader.GetBoolean("activo")
    };
}
