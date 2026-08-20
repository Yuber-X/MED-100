using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Reglas del tarifario.
///
/// La de fondo: un procedimiento que ya se facturó o que tiene citas NO se
/// borra, se desactiva. Borrarlo dejaría facturas y citas apuntando a algo
/// que la pantalla ya no sabe nombrar.
/// </summary>
public class ProcedimientoService
{
    /// <summary>
    /// Techo de duración: 12 horas. No es un límite del negocio, es un freno
    /// al dedazo — escribir 480 en vez de 48 llenaría la agenda del médico de
    /// un día entero sin que nadie lo note hasta que falte el turno.
    /// </summary>
    private const int DuracionMaximaMinutos = 12 * 60;

    private readonly ProcedimientoRepository _procedimientos;
    private readonly AuditoriaService _auditoria;

    public ProcedimientoService(ProcedimientoRepository procedimientos, AuditoriaService auditoria)
    {
        _procedimientos = procedimientos;
        _auditoria = auditoria;
    }

    public Task<List<Procedimiento>> ObtenerTodosAsync(CancellationToken ct = default) =>
        _procedimientos.ObtenerTodosAsync(ct);

    public Task<List<Procedimiento>> ObtenerActivosAsync(CancellationToken ct = default) =>
        _procedimientos.ObtenerActivosAsync(ct);

    public Task<Procedimiento?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _procedimientos.ObtenerPorIdAsync(id, ct);

    public async Task<long> CrearAsync(ProcedimientoDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: null, ct);
        var id = await _procedimientos.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Procedimiento, id,
            $"Procedimiento creado: {limpios.Nombre} (RD$ {limpios.Precio:N2}, " +
            $"{limpios.DuracionMinutos} min, {(limpios.ExentoItbis ? "exento" : "gravado")})", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, ProcedimientoDatos datos, CancellationToken ct = default)
    {
        var anterior = await _procedimientos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El procedimiento no existe o fue eliminado.");
        var limpios = await ValidarAsync(datos, exceptoId: id, ct);
        await _procedimientos.ActualizarAsync(id, limpios, ct);

        // El cambio de precio y el de exención quedan con nombre y apellido:
        // los dos mueven plata y el día que haya un reclamo hay que poder
        // decir desde cuándo rige lo que rige.
        var detalle = $"Procedimiento modificado: {limpios.Nombre}";
        if (anterior.Precio != limpios.Precio)
            detalle += $" · precio {anterior.Precio:N2} → {limpios.Precio:N2}";
        if (anterior.ExentoItbis != limpios.ExentoItbis)
            detalle += limpios.ExentoItbis ? " · pasó a EXENTO de ITBIS" : " · pasó a GRAVADO con ITBIS";
        if (anterior.DuracionMinutos != limpios.DuracionMinutos)
            detalle += $" · duración {anterior.DuracionMinutos} → {limpios.DuracionMinutos} min";
        if (anterior.Activo != limpios.Activo)
            detalle += limpios.Activo ? " · reactivado" : " · desactivado";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Procedimiento, id, detalle, ct);
    }

    /// <summary>
    /// Elimina (soft delete) un procedimiento que nunca se usó. Si ya se
    /// facturó o tiene citas, se niega y sugiere desactivarlo.
    /// </summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        ValidarPermiso();
        var procedimiento = await _procedimientos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El procedimiento no existe o ya fue eliminado.");

        if (await _procedimientos.FueFacturadoAsync(id, ct))
            throw new InvalidOperationException(
                $"«{procedimiento.Nombre}» ya se facturó alguna vez y no se puede eliminar.\n\n" +
                "Desactivalo: deja de aparecer para cobrar y para agendar, pero " +
                "las facturas que lo incluyen siguen completas.");

        if (await _procedimientos.TieneCitasAsync(id, ct))
            throw new InvalidOperationException(
                $"«{procedimiento.Nombre}» tiene citas registradas y no se puede eliminar.\n\n" +
                "Desactivalo para que no se pueda agendar más.");

        await _procedimientos.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Procedimiento, id,
            $"Procedimiento eliminado: {procedimiento.Nombre}", ct);
    }

    private async Task<ProcedimientoDatos> ValidarAsync(ProcedimientoDatos datos, long? exceptoId,
        CancellationToken ct)
    {
        ValidarPermiso();

        if (string.IsNullOrWhiteSpace(datos.Nombre))
            throw new ArgumentException("El nombre del procedimiento es obligatorio.");
        if (datos.Precio <= 0m)
            throw new ArgumentException("El precio debe ser mayor que cero.");
        if (datos.DuracionMinutos <= 0)
            throw new ArgumentException("La duración debe ser de al menos 1 minuto.");
        if (datos.DuracionMinutos > DuracionMaximaMinutos)
            throw new ArgumentException(
                $"La duración no puede pasar de {DuracionMaximaMinutos / 60} horas. " +
                "Si de verdad dura tanto, cargalo como varios procedimientos.");

        var codigo = Limpiar(datos.Codigo);
        if (codigo is not null && await _procedimientos.ExisteCodigoAsync(codigo, exceptoId, ct))
            throw new ArgumentException($"Ya existe un procedimiento con el código {codigo}.");

        return datos with
        {
            Codigo = codigo,
            Nombre = datos.Nombre.Trim(),
            Descripcion = Limpiar(datos.Descripcion)
        };
    }

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static void ValidarPermiso()
    {
        if (!SesionActual.TienePermiso("procedimientos"))
            throw new InvalidOperationException("No tienes permiso para gestionar el tarifario.");
    }
}
