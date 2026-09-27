using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Medicamentos indicados (013). ⚠ Contenido clínico: ver CLAUDE.md §1.1 y la
/// cabecera de scripts/db/013_indicaciones.sql.
///
/// El alta es ATÓMICA (cabecera + renglones en una transacción): una indicación
/// a medias —el paciente registrado pero sin los medicamentos— es peor que
/// ninguna, porque parece completa.
///
/// Todas las lecturas filtran <c>deleted_at IS NULL</c>. Nada se borra de
/// verdad: lo que se le indicó a un paciente es justo el tipo de dato que
/// alguien puede querer discutir después.
/// </summary>
public class IndicacionRepository
{
    private readonly ConexionFactory _factory;

    public IndicacionRepository(ConexionFactory factory) => _factory = factory;

    private const string SqlSelect = """
        SELECT i.id, i.cliente_id, i.medico_id, i.cita_id, i.fecha_utc,
               i.usuario_id, i.notas,
               c.nombre AS cliente_nombre,
               m.nombre AS medico_nombre,
               TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS usuario_nombre
        FROM indicacion i
        JOIN cliente c  ON c.id = i.cliente_id
        LEFT JOIN medico m ON m.id = i.medico_id
        JOIN usuario u  ON u.id = i.usuario_id
        WHERE i.deleted_at IS NULL
        """;

    /// <summary>
    /// Lo indicado en un día de negocio (UTC-4). Es la consulta principal: el
    /// pedido fue poder revisar "los procesos durante el día trabajado".
    /// </summary>
    public async Task<IReadOnlyList<Indicacion>> ObtenerDelDiaAsync(DateOnly dia,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SqlSelect}
              AND DATE(DATE_SUB(i.fecha_utc, INTERVAL 4 HOUR)) = @dia
            ORDER BY i.fecha_utc DESC, i.id DESC;
            """;
        cmd.Parameters.AddWithValue("@dia", dia.ToDateTime(TimeOnly.MinValue));
        return await LeerConMedicamentosAsync(conexion, cmd, ct);
    }

    /// <summary>Historial completo de un paciente, de lo más nuevo a lo más viejo.</summary>
    public async Task<IReadOnlyList<Indicacion>> ObtenerDeClienteAsync(long clienteId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SqlSelect}
              AND i.cliente_id = @clienteId
            ORDER BY i.fecha_utc DESC, i.id DESC;
            """;
        cmd.Parameters.AddWithValue("@clienteId", clienteId);
        return await LeerConMedicamentosAsync(conexion, cmd, ct);
    }

    /// <summary>Alta atómica: cabecera + renglones, o nada.</summary>
    public async Task<long> CrearAsync(Indicacion indicacion, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            long id;
            using (var cmd = conexion.CreateCommand())
            {
                cmd.Transaction = transaccion;
                cmd.CommandText = $"""
                    INSERT INTO {DbNames.Indicacion}
                      (cliente_id, medico_id, cita_id, fecha_utc, usuario_id, notas)
                    VALUES
                      (@clienteId, @medicoId, @citaId, @fecha, @usuarioId, @notas);
                    SELECT LAST_INSERT_ID();
                    """;
                cmd.Parameters.AddWithValue("@clienteId", indicacion.ClienteId);
                cmd.Parameters.AddWithValue("@medicoId", (object?)indicacion.MedicoId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@citaId", (object?)indicacion.CitaId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fecha", indicacion.FechaUtc);
                cmd.Parameters.AddWithValue("@usuarioId", indicacion.UsuarioId);
                cmd.Parameters.AddWithValue("@notas",
                    string.IsNullOrWhiteSpace(indicacion.Notas) ? DBNull.Value : indicacion.Notas.Trim());
                id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            }

            foreach (var m in indicacion.Medicamentos)
            {
                using var cmd = conexion.CreateCommand();
                cmd.Transaction = transaccion;
                cmd.CommandText = $"""
                    INSERT INTO {DbNames.IndicacionMedicamento}
                      (indicacion_id, medicamento, dosis, frecuencia, duracion, instrucciones)
                    VALUES
                      (@id, @medicamento, @dosis, @frecuencia, @duracion, @instrucciones);
                    """;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@medicamento", m.Medicamento.Trim());
                cmd.Parameters.AddWithValue("@dosis", Opcional(m.Dosis));
                cmd.Parameters.AddWithValue("@frecuencia", Opcional(m.Frecuencia));
                cmd.Parameters.AddWithValue("@duracion", Opcional(m.Duracion));
                cmd.Parameters.AddWithValue("@instrucciones", Opcional(m.Instrucciones));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await transaccion.CommitAsync(ct);
            return id;
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Los medicamentos que la clínica más indica, con la dosis y la frecuencia
    /// de la última vez (pedido 2026-09-21).
    ///
    /// Sale del historial y no de un catálogo aparte: así la lista se mantiene
    /// sola. La subconsulta agrupa por nombre para contar, y el JOIN de vuelta
    /// trae los datos del renglón MÁS NUEVO de cada medicamento — que es el que
    /// conviene proponer cuando la dosis habitual cambió.
    /// </summary>
    public async Task<IReadOnlyList<MedicamentoFrecuente>> ObtenerMasUsadosAsync(
        int tope = 30, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT u.medicamento, u.veces, m.dosis, m.frecuencia, m.duracion
            FROM (
                SELECT x.medicamento, COUNT(*) AS veces, MAX(x.id) AS ultimo_id
                FROM {DbNames.IndicacionMedicamento} x
                JOIN {DbNames.Indicacion} i
                  ON i.id = x.indicacion_id AND i.deleted_at IS NULL
                GROUP BY x.medicamento
            ) u
            JOIN {DbNames.IndicacionMedicamento} m ON m.id = u.ultimo_id
            ORDER BY u.veces DESC, u.medicamento
            LIMIT @tope;
            """;
        cmd.Parameters.AddWithValue("@tope", tope);

        var lista = new List<MedicamentoFrecuente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lista.Add(new MedicamentoFrecuente(
                reader.GetString("medicamento"),
                reader.GetInt32("veces"),
                Texto(reader, "dosis"),
                Texto(reader, "frecuencia"),
                Texto(reader, "duracion")));
        }
        return lista;

        static string? Texto(MySqlDataReader lector, string columna)
        {
            var valor = lector.IsDBNull(lector.GetOrdinal(columna))
                ? null : lector.GetString(columna);
            return string.IsNullOrWhiteSpace(valor) ? null : valor;
        }
    }

    /// <summary>
    /// Reescribe los medicamentos de una indicación ya guardada (pedido
    /// 2026-09-21: poder quitar o corregir uno solo de la lista).
    ///
    /// Se borran los renglones y se vuelven a escribir en UNA transacción: son
    /// de una sola indicación y valen juntos. Lo que había antes no se pierde
    /// —la auditoría lo guarda nombre por nombre, la escribe el servicio—, y
    /// por eso acá sí se borra de verdad: un renglón corregido tres veces
    /// dejaría tres filas muertas que nadie sabe leer.
    /// </summary>
    public async Task ReemplazarMedicamentosAsync(long indicacionId,
        IReadOnlyList<IndicacionMedicamento> medicamentos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            using (var borrar = conexion.CreateCommand())
            {
                borrar.Transaction = transaccion;
                borrar.CommandText =
                    $"DELETE FROM {DbNames.IndicacionMedicamento} WHERE indicacion_id = @id;";
                borrar.Parameters.AddWithValue("@id", indicacionId);
                await borrar.ExecuteNonQueryAsync(ct);
            }

            foreach (var m in medicamentos)
            {
                using var cmd = conexion.CreateCommand();
                cmd.Transaction = transaccion;
                cmd.CommandText = $"""
                    INSERT INTO {DbNames.IndicacionMedicamento}
                      (indicacion_id, medicamento, dosis, frecuencia, duracion, instrucciones)
                    VALUES
                      (@id, @medicamento, @dosis, @frecuencia, @duracion, @instrucciones);
                    """;
                cmd.Parameters.AddWithValue("@id", indicacionId);
                cmd.Parameters.AddWithValue("@medicamento", m.Medicamento.Trim());
                cmd.Parameters.AddWithValue("@dosis", Opcional(m.Dosis));
                cmd.Parameters.AddWithValue("@frecuencia", Opcional(m.Frecuencia));
                cmd.Parameters.AddWithValue("@duracion", Opcional(m.Duracion));
                cmd.Parameters.AddWithValue("@instrucciones", Opcional(m.Instrucciones));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            using (var tocar = conexion.CreateCommand())
            {
                tocar.Transaction = transaccion;
                tocar.CommandText =
                    $"UPDATE {DbNames.Indicacion} SET updated_at = UTC_TIMESTAMP() WHERE id = @id;";
                tocar.Parameters.AddWithValue("@id", indicacionId);
                await tocar.ExecuteNonQueryAsync(ct);
            }

            await transaccion.CommitAsync(ct);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Soft delete. El renglón sigue en la base para poder auditarlo.</summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Indicacion}
            SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static object Opcional(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();

    /// <summary>
    /// Lee las cabeceras y después TODOS sus medicamentos en una sola consulta.
    ///
    /// Una consulta por indicación sería N+1: con treinta pacientes en el día,
    /// treinta y una vueltas a la base para pintar una lista.
    /// </summary>
    private static async Task<IReadOnlyList<Indicacion>> LeerConMedicamentosAsync(
        MySqlConnection conexion, MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Indicacion>();
        using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                lista.Add(new Indicacion
                {
                    Id = reader.GetInt64("id"),
                    ClienteId = reader.GetInt64("cliente_id"),
                    MedicoId = reader.IsDBNull(reader.GetOrdinal("medico_id"))
                        ? null : reader.GetInt64("medico_id"),
                    CitaId = reader.IsDBNull(reader.GetOrdinal("cita_id"))
                        ? null : reader.GetInt64("cita_id"),
                    FechaUtc = DateTime.SpecifyKind(reader.GetDateTime("fecha_utc"), DateTimeKind.Utc),
                    UsuarioId = reader.GetInt64("usuario_id"),
                    Notas = reader.IsDBNull(reader.GetOrdinal("notas"))
                        ? null : reader.GetString("notas"),
                    ClienteNombre = reader.GetString("cliente_nombre"),
                    MedicoNombre = reader.IsDBNull(reader.GetOrdinal("medico_nombre"))
                        ? null : reader.GetString("medico_nombre"),
                    UsuarioNombre = reader.GetString("usuario_nombre")
                });
            }
        }

        if (lista.Count == 0)
            return lista;

        // Los ids salen de la lista que acabamos de leer, pero igual van como
        // parámetros: la regla del proyecto es que NADA se concatena en SQL,
        // sin excepciones por "este dato es nuestro".
        var porId = lista.ToDictionary(i => i.Id);
        var nombres = string.Join(",", porId.Keys.Select((_, n) => $"@id{n}"));

        using (var detalle = conexion.CreateCommand())
        {
            detalle.CommandText = $"""
                SELECT id, indicacion_id, medicamento, dosis, frecuencia, duracion, instrucciones
                FROM {DbNames.IndicacionMedicamento}
                WHERE indicacion_id IN ({nombres})
                ORDER BY id;
                """;
            var n = 0;
            foreach (var id in porId.Keys)
                detalle.Parameters.AddWithValue($"@id{n++}", id);

            using var reader = await detalle.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var indicacionId = reader.GetInt64("indicacion_id");
                if (!porId.TryGetValue(indicacionId, out var cabecera))
                    continue;

                cabecera.Medicamentos.Add(new IndicacionMedicamento
                {
                    Id = reader.GetInt64("id"),
                    IndicacionId = indicacionId,
                    Medicamento = reader.GetString("medicamento"),
                    Dosis = reader.IsDBNull(reader.GetOrdinal("dosis"))
                        ? null : reader.GetString("dosis"),
                    Frecuencia = reader.IsDBNull(reader.GetOrdinal("frecuencia"))
                        ? null : reader.GetString("frecuencia"),
                    Duracion = reader.IsDBNull(reader.GetOrdinal("duracion"))
                        ? null : reader.GetString("duracion"),
                    Instrucciones = reader.IsDBNull(reader.GetOrdinal("instrucciones"))
                        ? null : reader.GetString("instrucciones")
                });
            }
        }

        return lista;
    }
}
