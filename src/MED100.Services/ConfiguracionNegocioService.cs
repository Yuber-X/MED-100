using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Singleton con la configuración del negocio cargada al iniciar (patrón
/// similar a SesionActual, spec §8.5). Guardar exige permiso 'configuracion'
/// (EXCLUSIVO Admin, regla Yuber 2026-07-11) y queda auditado.
/// </summary>
public class ConfiguracionNegocioService
{
    private readonly ConfiguracionNegocioRepository _repositorio;
    private readonly AuditoriaService _auditoria;

    public ConfiguracionNegocioService(ConfiguracionNegocioRepository repositorio, AuditoriaService auditoria)
    {
        _repositorio = repositorio;
        _auditoria = auditoria;
    }

    /// <summary>Configuración vigente. Disponible tras CargarAsync (arranque).</summary>
    public ConfiguracionNegocio Actual { get; private set; } = new();

    /// <summary>Se dispara al guardar: las pantallas abiertas (Vender) se refrescan.</summary>
    public event Action? Cambiada;

    public async Task CargarAsync(CancellationToken ct = default) =>
        Actual = await _repositorio.ObtenerAsync(ct);

    public async Task GuardarAsync(ConfiguracionNegocio cfg, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("configuracion"))
            throw new InvalidOperationException("Solo el Administrador puede cambiar la configuración.");

        Validar(cfg);

        var anterior = Actual;
        await _repositorio.ActualizarAsync(cfg, ct);
        await CargarAsync(ct);

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.ConfiguracionNegocio, 1,
            DescribirCambios(anterior, Actual), ct);

        Cambiada?.Invoke();
    }

    private static void Validar(ConfiguracionNegocio cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.NombreNegocio))
            throw new ArgumentException("El nombre del negocio es obligatorio.");
        if (cfg.ItbisTasa is < 0m or > 100m)
            throw new ArgumentException("La tasa de ITBIS debe estar entre 0 y 100.");
        if (string.IsNullOrWhiteSpace(cfg.MonedaSimbolo))
            throw new ArgumentException("El símbolo de moneda es obligatorio.");
        if (string.IsNullOrWhiteSpace(cfg.FacturaPrefijo))
            throw new ArgumentException("El prefijo de factura es obligatorio.");
    }

    /// <summary>Resumen legible de lo que cambió (para el Historial).</summary>
    private static string DescribirCambios(ConfiguracionNegocio antes, ConfiguracionNegocio ahora)
    {
        var cambios = new List<string>();
        if (antes.NombreNegocio != ahora.NombreNegocio)
            cambios.Add($"nombre: \"{antes.NombreNegocio}\" → \"{ahora.NombreNegocio}\"");
        if (antes.Rnc != ahora.Rnc)
            cambios.Add($"RNC: {antes.Rnc ?? "(vacío)"} → {ahora.Rnc ?? "(vacío)"}");
        if (antes.ItbisActivo != ahora.ItbisActivo)
            cambios.Add($"ITBIS {(ahora.ItbisActivo ? "activado" : "DESACTIVADO")}");
        if (antes.ItbisTasa != ahora.ItbisTasa)
            cambios.Add($"tasa ITBIS: {antes.ItbisTasa:0.##}% → {ahora.ItbisTasa:0.##}%");
        if (antes.MostrarClienteEnVenta != ahora.MostrarClienteEnVenta)
            cambios.Add($"cliente en venta: {(ahora.MostrarClienteEnVenta ? "visible" : "oculto")}");
        if (antes.Redondeo != ahora.Redondeo)
            cambios.Add($"redondeo: {antes.Redondeo} → {ahora.Redondeo}");
        if (antes.FacturaPrefijo != ahora.FacturaPrefijo || antes.FacturaFormato != ahora.FacturaFormato)
            cambios.Add($"numeración: {ahora.FacturaPrefijo} ({ahora.FacturaFormato})");

        return cambios.Count == 0
            ? "Configuración guardada sin cambios"
            : "Configuración modificada — " + string.Join(" · ", cambios);
    }
}
