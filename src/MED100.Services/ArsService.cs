using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Catálogo de aseguradoras.
///
/// Una ARS con facturas NO se borra: se desactiva. Las facturas viejas la
/// referencian por FK y tienen que seguir pudiendo reimprimirse diciendo quién
/// cubrió qué.
/// </summary>
public class ArsService
{
    private readonly ArsRepository _ars;
    private readonly AuditoriaService _auditoria;

    public ArsService(ArsRepository ars, AuditoriaService auditoria)
    {
        _ars = ars;
        _auditoria = auditoria;
    }

    public Task<List<Ars>> ObtenerTodasAsync(CancellationToken ct = default) =>
        _ars.ObtenerTodasAsync(ct);

    public Task<List<Ars>> ObtenerActivasAsync(CancellationToken ct = default) =>
        _ars.ObtenerActivasAsync(ct);

    public Task<Ars?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _ars.ObtenerPorIdAsync(id, ct);

    public async Task<long> CrearAsync(ArsDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: null, ct);
        var id = await _ars.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Ars, id,
            $"ARS creada: {limpios.Nombre}", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, ArsDatos datos, CancellationToken ct = default)
    {
        var anterior = await _ars.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("La ARS no existe.");
        var limpios = await ValidarAsync(datos, exceptoId: id, ct);
        await _ars.ActualizarAsync(id, limpios, ct);

        var detalle = $"ARS modificada: {limpios.Nombre}";
        if (anterior.Activo != limpios.Activo)
            detalle += limpios.Activo ? " · reactivada" : " · desactivada";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Ars, id, detalle, ct);
    }

    private async Task<ArsDatos> ValidarAsync(ArsDatos datos, long? exceptoId, CancellationToken ct)
    {
        // Se gestiona con el mismo permiso que la configuración: quién cubre
        // qué es una decisión administrativa, no de mostrador.
        if (!SesionActual.TienePermiso("configuracion"))
            throw new InvalidOperationException("No tienes permiso para gestionar las aseguradoras.");

        var nombre = datos.Nombre?.Trim();
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre de la ARS es obligatorio.");
        if (await _ars.ExisteNombreAsync(nombre, exceptoId, ct))
            throw new ArgumentException($"Ya existe una ARS registrada con el nombre {nombre}.");

        return datos with
        {
            Nombre = nombre,
            Rnc = Limpiar(datos.Rnc),
            Telefono = Limpiar(datos.Telefono),
            Notas = Limpiar(datos.Notas)
        };
    }

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
