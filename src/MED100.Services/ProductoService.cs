using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Reglas de negocio de productos: nombre obligatorio, precio > 0,
/// cantidad ≥ 0, código único si viene. Requiere permiso productos.
/// </summary>
public class ProductoService
{
    private readonly ProductoRepository _productos;
    private readonly AuditoriaService _auditoria;

    public ProductoService(ProductoRepository productos, AuditoriaService auditoria)
    {
        _productos = productos;
        _auditoria = auditoria;
    }

    public Task<List<Producto>> ObtenerTodosAsync(CancellationToken ct = default) =>
        _productos.ObtenerTodosAsync(ct);

    public Task<List<Producto>> ObtenerConCaducidadAsync(CancellationToken ct = default) =>
        _productos.ObtenerConCaducidadAsync(ct);

    public Task<Producto?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _productos.ObtenerPorIdAsync(id, ct);

    public Task<AlmacenTotales> ObtenerTotalesAsync(CancellationToken ct = default) =>
        _productos.ObtenerTotalesAsync(ct);

    public async Task<long> CrearAsync(ProductoDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: null, ct);
        var id = await _productos.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Producto, id,
            $"Producto creado: {limpios.Nombre} (precio {limpios.Precio:0.00}, stock {limpios.Cantidad})", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, ProductoDatos datos, CancellationToken ct = default)
    {
        var anterior = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El producto no existe o fue eliminado.");
        var limpios = await ValidarAsync(datos, exceptoId: id, ct);
        await _productos.ActualizarAsync(id, limpios, ct);

        var detalle = $"Producto modificado: {limpios.Nombre}";
        if (anterior.Precio != limpios.Precio)
            detalle += $" · precio {anterior.Precio:0.00} → {limpios.Precio:0.00}";
        if (anterior.Cantidad != limpios.Cantidad)
            detalle += $" · stock {anterior.Cantidad} → {limpios.Cantidad}";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Producto, id, detalle, ct);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        ValidarPermiso();
        var producto = await _productos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El producto no existe o ya fue eliminado.");
        await _productos.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Producto, id,
            $"Producto eliminado: {producto.Nombre}", ct);
    }

    private async Task<ProductoDatos> ValidarAsync(ProductoDatos datos, long? exceptoId, CancellationToken ct)
    {
        ValidarPermiso();

        if (string.IsNullOrWhiteSpace(datos.Nombre))
            throw new ArgumentException("El nombre del producto es obligatorio.");
        if (datos.Precio <= 0m)
            throw new ArgumentException("El precio debe ser mayor que cero.");
        if (datos.Cantidad < 0)
            throw new ArgumentException("La cantidad no puede ser negativa.");

        var codigo = string.IsNullOrWhiteSpace(datos.Codigo) ? null : datos.Codigo.Trim();
        if (codigo is not null && await _productos.ExisteCodigoAsync(codigo, exceptoId, ct))
            throw new ArgumentException($"Ya existe un producto con el código {codigo}.");

        return datos with
        {
            Codigo = codigo,
            Nombre = datos.Nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(datos.Descripcion) ? null : datos.Descripcion.Trim()
        };
    }

    /// <summary>
    /// Cómo se lee el tipo en pantalla. Vive acá y no en el ViewModel para que
    /// la lista de Productos, el combo del formulario y el almacén digan todos
    /// lo mismo.
    /// </summary>
    public static string EtiquetaTipo(TipoProducto tipo) => tipo switch
    {
        TipoProducto.Insumo => "Insumo",
        TipoProducto.Medicamento => "Medicamento",
        TipoProducto.Material => "Material médico",
        TipoProducto.Equipo => "Equipo",
        TipoProducto.Limpieza => "Limpieza",
        TipoProducto.Oficina => "Oficina",
        TipoProducto.Otro => "Otro",
        _ => tipo.ToString()
    };

    private static void ValidarPermiso()
    {
        if (!SesionActual.TienePermiso("productos"))
            throw new InvalidOperationException("No tienes permiso para gestionar productos.");
    }
}
