using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// El historial de un paciente: sus citas, sus turnos, sus facturas y los
/// procedimientos que se le cobraron.
///
/// Vive en su propio repositorio y no dentro de ClienteRepository porque toca
/// cuatro tablas de tres módulos distintos; meterlo ahí convertiría el CRUD del
/// paciente en el cajón de sastre de la app.
///
/// <b>Lo que NO hay acá:</b> diagnósticos, tratamientos ni notas médicas. La
/// lista de "procedimientos" sale de lo que se FACTURÓ, no de un expediente —
/// MED-100 es la recepción (CLAUDE.md §1.1).
/// </summary>
public class HistorialRepository
{
    private readonly ConexionFactory _factory;

    public HistorialRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// Las cuatro listas en una sola ida a la base. Se manda todo junto porque
    /// la pantalla las muestra a la vez y cuatro viajes la llenarían por partes.
    /// </summary>
    public async Task<HistorialPaciente?> ObtenerAsync(long clienteId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.id, c.cedula, c.nombre, c.telefono, c.email, c.fecha_nacimiento,
                   c.sexo, c.direccion, c.referidor_id, r.nombre AS referidor_nombre,
                   c.notas, c.created_at, c.updated_at
            FROM {DbNames.Cliente} c
            LEFT JOIN {DbNames.Referidor} r ON r.id = c.referidor_id
            WHERE c.id = @id AND c.deleted_at IS NULL;

            SELECT ci.id, ci.cliente_id, cl.nombre AS paciente_nombre, cl.telefono AS paciente_telefono,
                   cl.email AS paciente_email, ci.medico_id, m.nombre AS medico_nombre,
                   ci.procedimiento_id, p.nombre AS procedimiento_nombre,
                   ci.fecha_hora, ci.duracion_minutos, ci.estado, ci.notas,
                   ci.recordatorio_enviado_at, ci.factura_id, ci.created_at, ci.updated_at
            FROM {DbNames.Cita} ci
            JOIN {DbNames.Cliente} cl ON cl.id = ci.cliente_id
            JOIN {DbNames.Medico}  m  ON m.id  = ci.medico_id
            LEFT JOIN {DbNames.Procedimiento} p ON p.id = ci.procedimiento_id
            WHERE ci.cliente_id = @id
            ORDER BY ci.fecha_hora DESC;

            SELECT t.id, t.fecha, t.numero, t.cliente_id, cl.nombre AS paciente_nombre,
                   t.medico_id, m.nombre AS medico_nombre, m.codigo_turno AS medico_codigo,
                   t.cita_id, t.estado, t.created_at, t.llamado_at
            FROM {DbNames.Turno} t
            LEFT JOIN {DbNames.Cliente} cl ON cl.id = t.cliente_id
            LEFT JOIN {DbNames.Medico}  m  ON m.id  = t.medico_id
            WHERE t.cliente_id = @id
            ORDER BY t.fecha DESC, t.numero DESC;

            SELECT f.id, f.numero_factura, f.fecha_emision, m.nombre AS medico_nombre,
                   f.total, f.paciente_paga, f.ars_cubierto, a.nombre AS ars_nombre,
                   f.metodo_pago, f.estado, f.ncf
            FROM {DbNames.Factura} f
            LEFT JOIN {DbNames.Medico} m ON m.id = f.medico_id
            LEFT JOIN {DbNames.Ars}    a ON a.id = f.ars_id
            WHERE f.cliente_id = @id
            ORDER BY f.fecha_emision DESC;

            SELECT f.fecha_emision, d.descripcion, d.cantidad, d.subtotal,
                   m.nombre AS medico_nombre, f.numero_factura, f.estado
            FROM {DbNames.Detalle} d
            JOIN {DbNames.Factura} f ON f.id = d.factura_id
            LEFT JOIN {DbNames.Medico} m ON m.id = f.medico_id
            WHERE f.cliente_id = @id AND d.procedimiento_id IS NOT NULL
            ORDER BY f.fecha_emision DESC;
            """;
        cmd.Parameters.AddWithValue("@id", clienteId);

        using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return null;
        var paciente = MapearCliente(reader);

        await reader.NextResultAsync(ct);
        var citas = new List<Cita>();
        while (await reader.ReadAsync(ct))
            citas.Add(MapearCita(reader));

        await reader.NextResultAsync(ct);
        var turnos = new List<Turno>();
        while (await reader.ReadAsync(ct))
            turnos.Add(MapearTurno(reader));

        await reader.NextResultAsync(ct);
        var facturas = new List<FacturaDePaciente>();
        while (await reader.ReadAsync(ct))
            facturas.Add(MapearFactura(reader));

        await reader.NextResultAsync(ct);
        var procedimientos = new List<ProcedimientoDePaciente>();
        while (await reader.ReadAsync(ct))
            procedimientos.Add(MapearProcedimiento(reader));

        return new HistorialPaciente(paciente, citas, turnos, facturas, procedimientos);
    }

    // ---------- Mapeo ----------

    private static string? Texto(MySqlDataReader r, string columna) =>
        r.IsDBNull(r.GetOrdinal(columna)) ? null : r.GetString(columna);

    private static long? Id(MySqlDataReader r, string columna) =>
        r.IsDBNull(r.GetOrdinal(columna)) ? null : r.GetInt64(columna);

    private static DateTime? FechaUtc(MySqlDataReader r, string columna) =>
        r.IsDBNull(r.GetOrdinal(columna))
            ? null
            : DateTime.SpecifyKind(r.GetDateTime(columna), DateTimeKind.Utc);

    private static Cliente MapearCliente(MySqlDataReader r) => new()
    {
        Id = r.GetInt64("id"),
        Cedula = Texto(r, "cedula"),
        Nombre = r.GetString("nombre"),
        Telefono = Texto(r, "telefono"),
        Email = Texto(r, "email"),
        FechaNacimiento = r.IsDBNull(r.GetOrdinal("fecha_nacimiento"))
            ? null : DateOnly.FromDateTime(r.GetDateTime("fecha_nacimiento")),
        Sexo = Texto(r, "sexo") is { } sexo ? EnumMap.SexoDeDb(sexo) : null,
        Direccion = Texto(r, "direccion"),
        ReferidorId = Id(r, "referidor_id"),
        ReferidorNombre = Texto(r, "referidor_nombre"),
        Notas = Texto(r, "notas"),
        CreatedAtUtc = DateTime.SpecifyKind(r.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = FechaUtc(r, "updated_at")
    };

    private static Cita MapearCita(MySqlDataReader r) => new()
    {
        Id = r.GetInt64("id"),
        ClienteId = r.GetInt64("cliente_id"),
        PacienteNombre = r.GetString("paciente_nombre"),
        PacienteTelefono = Texto(r, "paciente_telefono"),
        PacienteEmail = Texto(r, "paciente_email"),
        MedicoId = r.GetInt64("medico_id"),
        MedicoNombre = r.GetString("medico_nombre"),
        ProcedimientoId = Id(r, "procedimiento_id"),
        ProcedimientoNombre = Texto(r, "procedimiento_nombre"),
        FechaHoraUtc = DateTime.SpecifyKind(r.GetDateTime("fecha_hora"), DateTimeKind.Utc),
        DuracionMinutos = r.GetInt32("duracion_minutos"),
        Estado = EnumMap.EstadoCitaDeDb(r.GetString("estado")),
        Notas = Texto(r, "notas"),
        RecordatorioEnviadoAtUtc = FechaUtc(r, "recordatorio_enviado_at"),
        FacturaId = Id(r, "factura_id"),
        CreatedAtUtc = DateTime.SpecifyKind(r.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = FechaUtc(r, "updated_at")
    };

    private static Turno MapearTurno(MySqlDataReader r) => new()
    {
        Id = r.GetInt64("id"),
        Fecha = DateOnly.FromDateTime(r.GetDateTime("fecha")),
        Numero = r.GetInt32("numero"),
        ClienteId = Id(r, "cliente_id"),
        PacienteNombre = Texto(r, "paciente_nombre"),
        MedicoId = Id(r, "medico_id"),
        MedicoNombre = Texto(r, "medico_nombre"),
        MedicoCodigoTurno = Texto(r, "medico_codigo"),
        CitaId = Id(r, "cita_id"),
        Estado = EnumMap.EstadoTurnoDeDb(r.GetString("estado")),
        CreatedAtUtc = DateTime.SpecifyKind(r.GetDateTime("created_at"), DateTimeKind.Utc),
        LlamadoAtUtc = FechaUtc(r, "llamado_at")
    };

    private static FacturaDePaciente MapearFactura(MySqlDataReader r) => new(
        r.GetInt64("id"),
        r.GetString("numero_factura"),
        DateTime.SpecifyKind(r.GetDateTime("fecha_emision"), DateTimeKind.Utc),
        Texto(r, "medico_nombre"),
        r.GetDecimal("total"),
        r.GetDecimal("paciente_paga"),
        r.GetDecimal("ars_cubierto"),
        Texto(r, "ars_nombre"),
        EnumMap.MetodoPagoDeDb(r.GetString("metodo_pago")),
        EnumMap.EstadoFacturaDeDb(r.GetString("estado")),
        Texto(r, "ncf"));

    private static ProcedimientoDePaciente MapearProcedimiento(MySqlDataReader r) => new(
        DateTime.SpecifyKind(r.GetDateTime("fecha_emision"), DateTimeKind.Utc),
        r.GetString("descripcion"),
        r.GetInt32("cantidad"),
        r.GetDecimal("subtotal"),
        Texto(r, "medico_nombre"),
        r.GetString("numero_factura"),
        EnumMap.EstadoFacturaDeDb(r.GetString("estado")) == EstadoFactura.Anulada);
}
