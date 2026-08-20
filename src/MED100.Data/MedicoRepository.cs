using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// CRUD de médicos y de sus tramos de horario.
///
/// Los horarios se guardan en su propia tabla y no como texto dentro del
/// médico: es lo que permite preguntarle a SQL quién atiende un día y a una
/// hora, en vez de traerse todo y filtrar en memoria.
/// </summary>
public class MedicoRepository
{
    private readonly ConexionFactory _factory;

    public MedicoRepository(ConexionFactory factory) => _factory = factory;

    private const string ColumnasBase =
        "id, nombre, cedula, exequatur, especialidad, telefono, email, " +
        "porcentaje_honorario, codigo_turno, activo, created_at, updated_at";

    // ---------- Médicos ----------

    /// <summary>Todos los médicos vigentes, activos e inactivos (la UI los distingue).</summary>
    public async Task<List<Medico>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Medico}
            WHERE deleted_at IS NULL
            ORDER BY activo DESC, nombre;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    /// <summary>Solo los que pueden atender: alimenta los combos de cita y factura.</summary>
    public async Task<List<Medico>> ObtenerActivosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Medico}
            WHERE deleted_at IS NULL AND activo = 1
            ORDER BY nombre;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Medico?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Medico}
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// ¿Ese código de turno ya lo tiene otro médico? El índice único de la
    /// base es la garantía real; esto es para poder avisar con un mensaje
    /// entendible en vez de dejar salir el error 1062 de MySQL.
    /// </summary>
    public async Task<bool> ExisteCodigoTurnoAsync(string codigo, long? exceptoId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Medico}
            WHERE codigo_turno = @codigo AND deleted_at IS NULL
              AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<bool> ExisteCedulaAsync(string cedula, long? exceptoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Medico}
            WHERE cedula = @cedula AND deleted_at IS NULL
              AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@cedula", cedula);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<long> InsertarAsync(MedicoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Medico}
              (nombre, cedula, exequatur, especialidad, telefono, email,
               porcentaje_honorario, codigo_turno, activo)
            VALUES
              (@nombre, @cedula, @exequatur, @especialidad, @telefono, @email,
               @porcentaje, @codigoTurno, @activo);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, MedicoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Medico}
            SET nombre = @nombre, cedula = @cedula, exequatur = @exequatur,
                especialidad = @especialidad, telefono = @telefono, email = @email,
                porcentaje_honorario = @porcentaje, codigo_turno = @codigoTurno,
                activo = @activo,
                updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        AgregarParametros(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Medico} SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// ¿El médico ya facturó alguna vez? Un médico con facturas NO se borra:
    /// se desactiva. Borrarlo dejaría facturas apuntando a un médico que la
    /// pantalla ya no sabe nombrar.
    /// </summary>
    public async Task<bool> TieneFacturasAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {DbNames.Factura} WHERE medico_id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    // ---------- Horarios ----------

    public async Task<List<MedicoHorario>> ObtenerHorariosAsync(long medicoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, medico_id, dia_semana, hora_inicio, hora_fin
            FROM {DbNames.MedicoHorario}
            WHERE medico_id = @medicoId
            ORDER BY dia_semana, hora_inicio;
            """;
        cmd.Parameters.AddWithValue("@medicoId", medicoId);
        return await LeerHorariosAsync(cmd, ct);
    }

    /// <summary>Todos los tramos de todos los médicos: alimenta el tablero de "quién atiende ahora".</summary>
    public async Task<List<MedicoHorario>> ObtenerTodosLosHorariosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT h.id, h.medico_id, h.dia_semana, h.hora_inicio, h.hora_fin
            FROM {DbNames.MedicoHorario} h
            JOIN {DbNames.Medico} m ON m.id = h.medico_id AND m.deleted_at IS NULL
            ORDER BY h.medico_id, h.dia_semana, h.hora_inicio;
            """;
        return await LeerHorariosAsync(cmd, ct);
    }

    public async Task<long> InsertarHorarioAsync(long medicoId, HorarioDatos datos,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.MedicoHorario} (medico_id, dia_semana, hora_inicio, hora_fin)
            VALUES (@medicoId, @dia, @inicio, @fin);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@medicoId", medicoId);
        cmd.Parameters.AddWithValue("@dia", datos.DiaSemana);
        cmd.Parameters.AddWithValue("@inicio", datos.HoraInicio.ToTimeSpan());
        cmd.Parameters.AddWithValue("@fin", datos.HoraFin.ToTimeSpan());
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task EliminarHorarioAsync(long horarioId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        // Borrado REAL: un horario no es un dato histórico, es la agenda de hoy.
        cmd.CommandText = $"DELETE FROM {DbNames.MedicoHorario} WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", horarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------- Mapeo ----------

    private static async Task<List<Medico>> LeerListaAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Medico>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static async Task<List<MedicoHorario>> LeerHorariosAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<MedicoHorario>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new MedicoHorario
            {
                Id = reader.GetInt64("id"),
                MedicoId = reader.GetInt64("medico_id"),
                DiaSemana = reader.GetInt32("dia_semana"),
                HoraInicio = TimeOnly.FromTimeSpan(reader.GetTimeSpan("hora_inicio")),
                HoraFin = TimeOnly.FromTimeSpan(reader.GetTimeSpan("hora_fin"))
            });
        return lista;
    }

    private static Medico Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Nombre = reader.GetString("nombre"),
        Cedula = reader.IsDBNull(reader.GetOrdinal("cedula")) ? null : reader.GetString("cedula"),
        Exequatur = reader.IsDBNull(reader.GetOrdinal("exequatur")) ? null : reader.GetString("exequatur"),
        Especialidad = reader.IsDBNull(reader.GetOrdinal("especialidad")) ? null : reader.GetString("especialidad"),
        Telefono = reader.IsDBNull(reader.GetOrdinal("telefono")) ? null : reader.GetString("telefono"),
        Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
        PorcentajeHonorario = reader.GetDecimal("porcentaje_honorario"),
        CodigoTurno = reader.IsDBNull(reader.GetOrdinal("codigo_turno")) ? null : reader.GetString("codigo_turno"),
        Activo = reader.GetBoolean("activo"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };

    private static void AgregarParametros(MySqlCommand cmd, MedicoDatos datos)
    {
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@cedula", (object?)datos.Cedula ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@exequatur", (object?)datos.Exequatur ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@especialidad", (object?)datos.Especialidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telefono", (object?)datos.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)datos.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@porcentaje", datos.PorcentajeHonorario);
        cmd.Parameters.AddWithValue("@codigoTurno", (object?)datos.CodigoTurno ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", datos.Activo);
    }
}
