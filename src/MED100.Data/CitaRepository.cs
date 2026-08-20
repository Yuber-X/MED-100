using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// CRUD de la agenda.
///
/// <b>Acá es donde la hora local se vuelve UTC y al revés.</b> El resto del
/// sistema trabaja en local (es lo que el usuario escribe y lee) y la base
/// guarda UTC, como todo. La conversión vive en un solo lugar a propósito: dos
/// sitios donde convertir son dos sitios donde equivocarse por cuatro horas.
/// </summary>
public class CitaRepository
{
    private readonly ConexionFactory _factory;

    public CitaRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Los nombres vienen por JOIN y no copiados: una cita muestra siempre el
    /// nombre ACTUAL del paciente y del médico. Es la diferencia con la
    /// factura, donde los datos se congelan al emitirla.
    /// </summary>
    private const string SelectBase = $"""
        SELECT ci.id, ci.cliente_id, cl.nombre AS paciente_nombre,
               cl.telefono AS paciente_telefono, cl.email AS paciente_email,
               ci.medico_id, m.nombre AS medico_nombre,
               ci.procedimiento_id, p.nombre AS procedimiento_nombre,
               ci.fecha_hora, ci.duracion_minutos, ci.estado, ci.notas,
               ci.recordatorio_enviado_at, ci.factura_id, ci.created_at, ci.updated_at
        FROM {DbNames.Cita} ci
        JOIN {DbNames.Cliente} cl ON cl.id = ci.cliente_id
        JOIN {DbNames.Medico}  m  ON m.id  = ci.medico_id
        LEFT JOIN {DbNames.Procedimiento} p ON p.id = ci.procedimiento_id
        """;

    /// <summary>
    /// Citas de un día de negocio (hora local de RD). El rango se calcula en
    /// UTC porque es lo que hay en la columna: el día local del 12 de agosto va
    /// desde las 04:00 UTC del 12 hasta las 04:00 UTC del 13.
    /// </summary>
    public async Task<List<Cita>> ObtenerPorDiaAsync(DateOnly dia, long? medicoId = null,
        CancellationToken ct = default)
    {
        var desdeUtc = FechaNegocio.ALocalUtc(dia.ToDateTime(TimeOnly.MinValue));
        var hastaUtc = FechaNegocio.ALocalUtc(dia.AddDays(1).ToDateTime(TimeOnly.MinValue));

        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE ci.fecha_hora >= @desde AND ci.fecha_hora < @hasta
              AND (@medicoId IS NULL OR ci.medico_id = @medicoId)
            ORDER BY ci.fecha_hora, m.nombre;
            """;
        cmd.Parameters.AddWithValue("@desde", desdeUtc);
        cmd.Parameters.AddWithValue("@hasta", hastaUtc);
        cmd.Parameters.AddWithValue("@medicoId", (object?)medicoId ?? DBNull.Value);
        return await LeerListaAsync(cmd, ct);
    }

    /// <summary>
    /// Citas de un médico en un día. Es lo que alimenta la validación de choques
    /// y el cálculo de huecos libres.
    /// </summary>
    public async Task<List<Cita>> ObtenerDelMedicoEnDiaAsync(long medicoId, DateOnly dia,
        CancellationToken ct = default)
    {
        // Se abre un día para cada lado: una cita que empieza a las 11:30 PM del
        // día anterior y dura una hora se pisa con las 12:15 AM de hoy.
        var desdeUtc = FechaNegocio.ALocalUtc(dia.AddDays(-1).ToDateTime(TimeOnly.MinValue));
        var hastaUtc = FechaNegocio.ALocalUtc(dia.AddDays(2).ToDateTime(TimeOnly.MinValue));

        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE ci.medico_id = @medicoId
              AND ci.fecha_hora >= @desde AND ci.fecha_hora < @hasta
            ORDER BY ci.fecha_hora;
            """;
        cmd.Parameters.AddWithValue("@medicoId", medicoId);
        cmd.Parameters.AddWithValue("@desde", desdeUtc);
        cmd.Parameters.AddWithValue("@hasta", hastaUtc);
        return await LeerListaAsync(cmd, ct);
    }

    /// <summary>Historial de citas de un paciente, de la más reciente a la más vieja.</summary>
    public async Task<List<Cita>> ObtenerDelPacienteAsync(long clienteId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE ci.cliente_id = @clienteId
            ORDER BY ci.fecha_hora DESC;
            """;
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Cita?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"{SelectBase} WHERE ci.id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// Citas que todavía no recibieron recordatorio y caen dentro de la ventana
    /// pedida. Filtra por estado: a una cita cancelada no se le manda nada, y a
    /// una atendida tampoco.
    /// </summary>
    public async Task<List<Cita>> ObtenerPendientesDeRecordatorioAsync(
        DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE ci.recordatorio_enviado_at IS NULL
              AND ci.estado IN ('programada', 'confirmada')
              AND ci.fecha_hora >= @desde AND ci.fecha_hora < @hasta
              AND cl.email IS NOT NULL AND cl.email <> ''
              AND cl.deleted_at IS NULL
            ORDER BY ci.fecha_hora;
            """;
        cmd.Parameters.AddWithValue("@desde", desdeUtc);
        cmd.Parameters.AddWithValue("@hasta", hastaUtc);
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<long> InsertarAsync(CitaDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Cita}
              (cliente_id, medico_id, procedimiento_id, fecha_hora, duracion_minutos, estado, notas)
            VALUES
              (@cliente, @medico, @procedimiento, @fechaHora, @duracion, @estado, @notas);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, CitaDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Cita}
            SET cliente_id = @cliente, medico_id = @medico, procedimiento_id = @procedimiento,
                fecha_hora = @fechaHora, duracion_minutos = @duracion, estado = @estado,
                notas = @notas, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        AgregarParametros(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(long id, EstadoCita estado, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Cita} SET estado = @estado, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(estado));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Marca el recordatorio como enviado. Se llama DESPUÉS de que el correo
    /// salió bien: marcarlo antes haría que un fallo de red dejara al paciente
    /// sin aviso y al sistema convencido de habérselo mandado.
    /// </summary>
    public async Task MarcarRecordatorioEnviadoAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Cita} SET recordatorio_enviado_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Borrado REAL, y solo para citas que nunca se cobraron. Una cita no es un
    /// documento fiscal: si se agendó por error, se borra. Lo que no se borra
    /// nunca es la factura, y por eso las que tienen <c>factura_id</c> quedan
    /// fuera (además la FK lo impediría).
    /// </summary>
    public async Task<bool> EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DELETE FROM {DbNames.Cita} WHERE id = @id AND factura_id IS NULL;";
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ---------- Mapeo ----------

    private static async Task<List<Cita>> LeerListaAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Cita>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static Cita Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        ClienteId = reader.GetInt64("cliente_id"),
        PacienteNombre = reader.GetString("paciente_nombre"),
        PacienteTelefono = reader.IsDBNull(reader.GetOrdinal("paciente_telefono"))
            ? null : reader.GetString("paciente_telefono"),
        PacienteEmail = reader.IsDBNull(reader.GetOrdinal("paciente_email"))
            ? null : reader.GetString("paciente_email"),
        MedicoId = reader.GetInt64("medico_id"),
        MedicoNombre = reader.GetString("medico_nombre"),
        ProcedimientoId = reader.IsDBNull(reader.GetOrdinal("procedimiento_id"))
            ? null : reader.GetInt64("procedimiento_id"),
        ProcedimientoNombre = reader.IsDBNull(reader.GetOrdinal("procedimiento_nombre"))
            ? null : reader.GetString("procedimiento_nombre"),
        FechaHoraUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_hora"), DateTimeKind.Utc),
        DuracionMinutos = reader.GetInt32("duracion_minutos"),
        Estado = EnumMap.EstadoCitaDeDb(reader.GetString("estado")),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        RecordatorioEnviadoAtUtc = reader.IsDBNull(reader.GetOrdinal("recordatorio_enviado_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("recordatorio_enviado_at"), DateTimeKind.Utc),
        FacturaId = reader.IsDBNull(reader.GetOrdinal("factura_id"))
            ? null : reader.GetInt64("factura_id"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };

    private static void AgregarParametros(MySqlCommand cmd, CitaDatos datos)
    {
        cmd.Parameters.AddWithValue("@cliente", datos.ClienteId);
        cmd.Parameters.AddWithValue("@medico", datos.MedicoId);
        cmd.Parameters.AddWithValue("@procedimiento", (object?)datos.ProcedimientoId ?? DBNull.Value);
        // Acá y solo acá: la hora que escribió el usuario pasa a UTC.
        cmd.Parameters.AddWithValue("@fechaHora", FechaNegocio.ALocalUtc(datos.FechaHoraLocal));
        cmd.Parameters.AddWithValue("@duracion", datos.DuracionMinutos);
        cmd.Parameters.AddWithValue("@estado", EnumMap.ADb(datos.Estado));
        cmd.Parameters.AddWithValue("@notas", (object?)datos.Notas ?? DBNull.Value);
    }
}
