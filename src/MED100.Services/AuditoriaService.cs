using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Punto único para registrar auditoría. TODA mutación (crear/modificar/eliminar)
/// de cliente, préstamo, cuota, pago o usuario pasa por aquí.
/// </summary>
public class AuditoriaService
{
    private readonly AuditoriaRepository _repositorio;

    public AuditoriaService(AuditoriaRepository repositorio) => _repositorio = repositorio;

    /// <summary>Registra una entrada con conexión propia (operaciones simples).</summary>
    public Task RegistrarAsync(AccionAuditoria accion, string entidad, long? entidadId,
        string? descripcion, CancellationToken ct = default) =>
        _repositorio.InsertarAsync(Construir(accion, entidad, entidadId, descripcion), ct);

    /// <summary>
    /// Registra dentro de una transacción existente. Usar en operaciones multi-paso
    /// (crear préstamo, registrar pago): la auditoría entra en la MISMA transacción.
    /// </summary>
    public Task RegistrarEnTransaccionAsync(AccionAuditoria accion, string entidad, long? entidadId,
        string? descripcion, MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default) =>
        _repositorio.InsertarAsync(Construir(accion, entidad, entidadId, descripcion), conexion, transaccion, ct);

    /// <summary>Visor del Historial (solo lectura, con filtros).</summary>
    public Task<IReadOnlyList<Auditoria>> BuscarAsync(FiltroAuditoria filtro, CancellationToken ct = default) =>
        _repositorio.BuscarAsync(filtro, ct);

    private static Auditoria Construir(AccionAuditoria accion, string entidad, long? entidadId, string? descripcion)
    {
        if (!SesionActual.HaySesionActiva)
            throw new InvalidOperationException("No se puede auditar sin sesión activa.");

        return new Auditoria
        {
            UsuarioId = SesionActual.Id,
            Entidad = entidad,
            EntidadId = entidadId,
            Accion = accion,
            Descripcion = descripcion,
            TimestampUtc = DateTime.UtcNow
        };
    }
}
