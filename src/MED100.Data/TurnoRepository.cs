using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Turnos de la sala de espera.
///
/// El número reinicia todos los días y se reserva de forma ATÓMICA, igual que
/// el número de factura: dos recepcionistas dando turno al mismo tiempo no
/// pueden sacar el mismo número, porque la gente de la sala se guía por ese
/// papel y dos "15" es una discusión en el mostrador.
/// </summary>
public class TurnoRepository
{
    // Códigos de MySQL que significan "chocaste con otro, volvé a intentar".
    private const int ErrorDuplicado = 1062;   // clave duplicada
    private const int ErrorDeadlock = 1213;    // deadlock
    private const int ErrorEsperaLock = 1205;  // lock wait timeout

    /// <summary>
    /// Reintentos de la reserva. Con el número de turno chocando de frente
    /// (varias recepcionistas a la vez) unos pocos choques son normales; lo que
    /// no puede pasar es que salgan dos papelitos con el mismo número.
    /// </summary>
    private const int IntentosReserva = 10;

    private readonly ConexionFactory _factory;

    public TurnoRepository(ConexionFactory factory) => _factory = factory;

    private const string SelectBase = $"""
        SELECT t.id, t.fecha, t.numero, t.cliente_id, cl.nombre AS paciente_nombre,
               t.medico_id, m.nombre AS medico_nombre, m.codigo_turno AS medico_codigo,
               t.cita_id, t.estado,
               t.created_at, t.llamado_at
        FROM {DbNames.Turno} t
        LEFT JOIN {DbNames.Cliente} cl ON cl.id = t.cliente_id
        LEFT JOIN {DbNames.Medico}  m  ON m.id  = t.medico_id
        """;

    /// <summary>
    /// Da el siguiente turno del día. Devuelve el turno ya creado.
    ///
    /// Quien manda de verdad acá es <c>uq_turno_fecha_numero</c>: si dos
    /// recepcionistas calculan el mismo número, la base rechaza al segundo y
    /// este método reintenta. La unicidad no depende de que el bloqueo salga
    /// bien, depende de la restricción.
    /// </summary>
    public async Task<Turno> DarSiguienteAsync(DateOnly fecha, TurnoDatos datos,
        CancellationToken ct = default)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                return await IntentarDarAsync(fecha, datos, ct);
            }
            catch (MySqlException ex) when (EsChoque(ex) && intento < IntentosReserva)
            {
                // Otro se quedó con ese número. Espera corta y creciente para
                // no volver a chocar los dos en el mismo instante.
                await Task.Delay(5 * intento, ct);
            }
        }
    }

    private static bool EsChoque(MySqlException ex) =>
        ex.Number is ErrorDuplicado or ErrorDeadlock or ErrorEsperaLock;

    /// <summary>
    /// UN SOLO statement: calcula el número y lo inserta de una.
    ///
    /// La versión anterior hacía SELECT ... FOR UPDATE y después INSERT en una
    /// transacción abierta. Era correcta en cuanto a unicidad pero se
    /// <b>deadlockeaba</b> con varias recepcionistas a la vez: el FOR UPDATE
    /// sobre un rango vacío toma un gap lock compartido, varias transacciones
    /// lo consiguen, y todas se traban al querer insertar. Reproducido con diez
    /// turnos en paralelo contra MySQL real.
    ///
    /// Con un statement único no queda ninguna transacción abierta esperando, y
    /// la restricción UNIQUE sigue garantizando que no salgan dos "15".
    /// </summary>
    private async Task<Turno> IntentarDarAsync(DateOnly fecha, TurnoDatos datos, CancellationToken ct)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        // La subconsulta va envuelta en una derivada: MySQL no deja leer
        // directamente de la misma tabla en la que se está insertando.
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Turno} (fecha, numero, cliente_id, medico_id, cita_id)
            SELECT @fecha, COALESCE(MAX(numero), 0) + 1, @cliente, @medico, @cita
            FROM (SELECT numero FROM {DbNames.Turno} WHERE fecha = @fecha) AS delDia;
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@cliente", (object?)datos.ClienteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@medico", (object?)datos.MedicoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cita", (object?)datos.CitaId ?? DBNull.Value);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));

        return await ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El turno se creó pero no se pudo leer.");
    }

    public async Task<List<Turno>> ObtenerPorDiaAsync(DateOnly fecha, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE t.fecha = @fecha
            ORDER BY t.numero;
            """;
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Turno?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"{SelectBase} WHERE t.id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>El que sigue: el de menor número que todavía espera.</summary>
    public async Task<Turno?> SiguienteEnEsperaAsync(DateOnly fecha, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE t.fecha = @fecha AND t.estado = 'esperando'
            ORDER BY t.numero
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// Cambia el estado. <c>llamado_at</c> se sella la PRIMERA vez que se llama
    /// y no se vuelve a tocar: sirve para saber cuánto esperó la gente, y
    /// pisarlo al marcarlo atendido borraría justamente ese dato.
    /// </summary>
    public async Task CambiarEstadoAsync(long id, EstadoTurno estado, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Turno}
            SET estado = @estado,
                llamado_at = CASE WHEN @sellar = 1 AND llamado_at IS NULL
                                  THEN UTC_TIMESTAMP() ELSE llamado_at END
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@sellar", estado == EstadoTurno.Llamado ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Asocia el turno a un paciente que se registró después de entrar.</summary>
    public async Task AsignarPacienteAsync(long id, long clienteId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Turno} SET cliente_id = @cliente WHERE id = @id;";
        cmd.Parameters.AddWithValue("@cliente", clienteId);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Conteo por estado, calculado en SQL. Alimenta el tablero.</summary>
    public async Task<ResumenSala> ResumenAsync(DateOnly fecha, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT
              SUM(estado = 'esperando') AS esperando,
              SUM(estado = 'llamado')   AS llamados,
              SUM(estado = 'atendido')  AS atendidos,
              SUM(estado = 'ausente')   AS ausentes
            FROM {DbNames.Turno}
            WHERE fecha = @fecha;
            """;
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new ResumenSala(0, 0, 0, 0);

        // Sin filas, SUM devuelve NULL en vez de 0.
        return new ResumenSala(Entero(reader, 0), Entero(reader, 1), Entero(reader, 2), Entero(reader, 3));
    }

    private static int Entero(MySqlDataReader reader, int columna) =>
        reader.IsDBNull(columna) ? 0 : Convert.ToInt32(reader.GetValue(columna));

    private static async Task<List<Turno>> LeerListaAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Turno>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static Turno Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Fecha = DateOnly.FromDateTime(reader.GetDateTime("fecha")),
        Numero = reader.GetInt32("numero"),
        ClienteId = reader.IsDBNull(reader.GetOrdinal("cliente_id")) ? null : reader.GetInt64("cliente_id"),
        PacienteNombre = reader.IsDBNull(reader.GetOrdinal("paciente_nombre"))
            ? null : reader.GetString("paciente_nombre"),
        MedicoId = reader.IsDBNull(reader.GetOrdinal("medico_id")) ? null : reader.GetInt64("medico_id"),
        MedicoNombre = reader.IsDBNull(reader.GetOrdinal("medico_nombre"))
            ? null : reader.GetString("medico_nombre"),
        MedicoCodigoTurno = reader.IsDBNull(reader.GetOrdinal("medico_codigo"))
            ? null : reader.GetString("medico_codigo"),
        CitaId = reader.IsDBNull(reader.GetOrdinal("cita_id")) ? null : reader.GetInt64("cita_id"),
        Estado = EnumMap.EstadoTurnoDeDb(reader.GetString("estado")),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        LlamadoAtUtc = reader.IsDBNull(reader.GetOrdinal("llamado_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("llamado_at"), DateTimeKind.Utc)
    };
}
