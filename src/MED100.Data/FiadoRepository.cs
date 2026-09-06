using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Deudas de pacientes (012).
///
/// El saldo se calcula SIEMPRE en SQL —<c>paciente_paga − abonado_inicial −
/// SUM(abonos)</c>— y nunca se guarda. Un saldo persistido se desincroniza de
/// sus propios pagos, y cuando eso pasa no hay forma de saber cuál de los dos
/// miente. Traerlo calculado también evita cargar todos los abonos de todas las
/// facturas para poder pintar una lista.
///
/// Las facturas ANULADAS quedan fuera de todo lo de acá: una factura anulada no
/// se cobra, se corrige.
/// </summary>
public class FiadoRepository
{
    private readonly ConexionFactory _factory;

    public FiadoRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Lo pagado en total de una factura (mostrador + abonos). Se usa para
    /// revalidar el saldo justo antes de aceptar un abono.
    /// </summary>
    private const string SqlPagado =
        "f.abonado_inicial + COALESCE((SELECT SUM(a.monto) FROM factura_abono a " +
        "WHERE a.factura_id = f.id), 0.00)";

    private const string SqlSelectFiado = $"""
        SELECT f.id, f.numero_factura, f.fecha_emision, f.cliente_id,
               COALESCE(c.nombre, 'Consumidor final') AS cliente_nombre,
               c.telefono                             AS cliente_telefono,
               f.paciente_paga,
               {SqlPagado}                            AS pagado,
               f.fecha_compromiso
        FROM factura f
        LEFT JOIN cliente c ON c.id = f.cliente_id
        """;

    /// <summary>
    /// Todo lo que sigue debiéndose, lo más urgente primero.
    ///
    /// Las que no tienen fecha acordada van al final y no adelante: no están
    /// atrasadas, y ponerlas arriba taparía a las que sí hay que reclamar hoy.
    /// </summary>
    public async Task<IReadOnlyList<FiadoResumen>> ObtenerPendientesAsync(
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SqlSelectFiado}
            WHERE f.estado = 'emitida'
              AND {SqlPagado} < f.paciente_paga
            ORDER BY f.fecha_compromiso IS NULL, f.fecha_compromiso, f.fecha_emision;
            """;
        return await LeerAsync(cmd, ct);
    }

    /// <summary>Deudas pendientes de un paciente, para su ficha.</summary>
    public async Task<IReadOnlyList<FiadoResumen>> ObtenerPendientesDeClienteAsync(
        long clienteId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SqlSelectFiado}
            WHERE f.estado = 'emitida'
              AND f.cliente_id = @clienteId
              AND {SqlPagado} < f.paciente_paga
            ORDER BY f.fecha_compromiso IS NULL, f.fecha_compromiso, f.fecha_emision;
            """;
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        return await LeerAsync(cmd, ct);
    }

    public async Task<FiadoResumen?> ObtenerPorFacturaAsync(long facturaId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SqlSelectFiado}
            WHERE f.id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", facturaId);
        return (await LeerAsync(cmd, ct)).FirstOrDefault();
    }

    /// <summary>Los abonos de una factura, del más viejo al más nuevo.</summary>
    public async Task<IReadOnlyList<FacturaAbono>> ObtenerAbonosAsync(long facturaId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT a.id, a.factura_id, a.usuario_id, a.fecha_utc, a.monto,
                   a.metodo_pago, a.notas,
                   TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS usuario_nombre
            FROM {DbNames.FacturaAbono} a
            JOIN {DbNames.Usuario} u ON u.id = a.usuario_id
            WHERE a.factura_id = @facturaId
            ORDER BY a.fecha_utc, a.id;
            """;
        cmd.Parameters.AddWithValue("@facturaId", facturaId);

        var lista = new List<FacturaAbono>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new FacturaAbono
            {
                Id = reader.GetInt64("id"),
                FacturaId = reader.GetInt64("factura_id"),
                UsuarioId = reader.GetInt64("usuario_id"),
                FechaUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_utc"), DateTimeKind.Utc),
                Monto = reader.GetDecimal("monto"),
                MetodoPago = EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
                Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
                UsuarioNombre = reader.GetString("usuario_nombre")
            });
        }
        return lista;
    }

    /// <summary>
    /// Registra un abono. Devuelve el saldo que queda después.
    ///
    /// Todo dentro de UNA transacción con <c>FOR UPDATE</c> sobre la factura:
    /// dos recepcionistas cobrando la misma deuda a la vez no pueden aceptar
    /// entre las dos más de lo que se debe. Sin el bloqueo, las dos leerían el
    /// mismo saldo y las dos lo darían por bueno.
    /// </summary>
    public async Task<decimal> RegistrarAbonoAsync(long facturaId, long usuarioId,
        decimal monto, MetodoPagoFactura metodo, string? notas, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            decimal pacientePaga, pagado;
            string estado;
            using (var select = conexion.CreateCommand())
            {
                select.Transaction = transaccion;
                select.CommandText = $"""
                    SELECT f.estado, f.paciente_paga, {SqlPagado} AS pagado
                    FROM {DbNames.Factura} f
                    WHERE f.id = @id
                    FOR UPDATE;
                    """;
                select.Parameters.AddWithValue("@id", facturaId);
                using var reader = await select.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new InvalidOperationException("La factura no existe.");
                estado = reader.GetString("estado");
                pacientePaga = reader.GetDecimal("paciente_paga");
                pagado = reader.GetDecimal("pagado");
            }

            if (estado != "emitida")
                throw new InvalidOperationException(
                    "Esa factura está anulada: no se le pueden cobrar abonos.");

            var saldo = pacientePaga - pagado;
            if (saldo <= 0m)
                throw new InvalidOperationException("Esa factura ya está pagada por completo.");
            if (monto > saldo)
                throw new InvalidOperationException(
                    $"El abono ({monto:0.00}) es mayor que el saldo ({saldo:0.00}). " +
                    "Cobrá el saldo exacto o menos.");

            using (var insert = conexion.CreateCommand())
            {
                insert.Transaction = transaccion;
                insert.CommandText = $"""
                    INSERT INTO {DbNames.FacturaAbono}
                      (factura_id, usuario_id, fecha_utc, monto, metodo_pago, notas)
                    VALUES
                      (@facturaId, @usuarioId, @fecha, @monto, @metodo, @notas);
                    """;
                insert.Parameters.AddWithValue("@facturaId", facturaId);
                insert.Parameters.AddWithValue("@usuarioId", usuarioId);
                insert.Parameters.AddWithValue("@fecha", DateTime.UtcNow);
                insert.Parameters.AddWithValue("@monto", monto);
                insert.Parameters.AddWithValue("@metodo", EnumMap.ADb(metodo));
                insert.Parameters.AddWithValue("@notas",
                    string.IsNullOrWhiteSpace(notas) ? DBNull.Value : notas.Trim());
                await insert.ExecuteNonQueryAsync(ct);
            }

            await transaccion.CommitAsync(ct);
            return saldo - monto;
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Cambia la fecha acordada de una deuda que sigue abierta. No toca plata:
    /// es el caso de "pasa el lunes" que se convierte en "pasa el viernes".
    /// </summary>
    public async Task ActualizarCompromisoAsync(long facturaId, DateOnly nueva,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Factura}
            SET fecha_compromiso = @fecha
            WHERE id = @id AND estado = 'emitida';
            """;
        cmd.Parameters.AddWithValue("@fecha", nueva.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@id", facturaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<FiadoResumen>> LeerAsync(
        MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<FiadoResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new FiadoResumen
            {
                FacturaId = reader.GetInt64("id"),
                NumeroFactura = reader.GetString("numero_factura"),
                FechaEmisionUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_emision"),
                    DateTimeKind.Utc),
                ClienteId = reader.IsDBNull(reader.GetOrdinal("cliente_id"))
                    ? null : reader.GetInt64("cliente_id"),
                ClienteNombre = reader.GetString("cliente_nombre"),
                ClienteTelefono = reader.IsDBNull(reader.GetOrdinal("cliente_telefono"))
                    ? null : reader.GetString("cliente_telefono"),
                PacientePaga = reader.GetDecimal("paciente_paga"),
                Pagado = reader.GetDecimal("pagado"),
                FechaCompromiso = reader.IsDBNull(reader.GetOrdinal("fecha_compromiso"))
                    ? null : DateOnly.FromDateTime(reader.GetDateTime("fecha_compromiso"))
            });
        }
        return lista;
    }
}
