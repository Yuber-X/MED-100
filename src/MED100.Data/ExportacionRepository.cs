using MySqlConnector;
using MED100.Common;

namespace MED100.Data;

/// <summary>Una tabla lista para exportar: encabezados + filas de celdas.</summary>
public record TablaExportada(string Nombre, IReadOnlyList<string> Encabezados, IReadOnlyList<object?[]> Filas);

/// <summary>
/// Lecturas completas para la exportación a Excel (consulta y respaldo legible).
/// Columnas explícitas siempre — nada de SELECT *.
/// NOTA: password_hash NUNCA se exporta (regla de seguridad).
/// </summary>
public class ExportacionRepository
{
    private readonly ConexionFactory _factory;

    public ExportacionRepository(ConexionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<TablaExportada>> ObtenerTodoAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        return
        [
            await LeerAsync(conexion, "Clientes", $"""
                SELECT id, cedula, nombre, telefono, direccion, notas,
                       created_at, updated_at, deleted_at
                FROM {DbNames.Cliente} ORDER BY id;
                """, ct),
            await LeerAsync(conexion, "Productos", $"""
                SELECT id, codigo, nombre, precio, cantidad, descripcion, fecha_caducidad,
                       created_at, updated_at, deleted_at
                FROM {DbNames.Producto} ORDER BY id;
                """, ct),
            await LeerAsync(conexion, "Facturas", $"""
                SELECT f.id, f.numero_factura, f.fecha_emision, c.nombre AS cliente,
                       TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS cajero,
                       f.subtotal, f.itbis_tasa, f.itbis, f.total, f.metodo_pago,
                       f.efectivo_recibido, f.cambio, f.estado, f.anulada_at, f.anulada_motivo
                FROM {DbNames.Factura} f
                LEFT JOIN {DbNames.Cliente} c ON c.id = f.cliente_id
                JOIN {DbNames.Usuario} u ON u.id = f.usuario_id
                ORDER BY f.id;
                """, ct),
            await LeerAsync(conexion, "Detalle de facturas", $"""
                SELECT d.id, f.numero_factura, p.nombre AS producto,
                       d.cantidad, d.precio_unitario, d.subtotal
                FROM {DbNames.Detalle} d
                JOIN {DbNames.Factura} f ON f.id = d.factura_id
                JOIN {DbNames.Producto} p ON p.id = d.producto_id
                ORDER BY d.factura_id, d.id;
                """, ct),
            await LeerAsync(conexion, "Cuadres de caja", $"""
                SELECT cc.id, TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS cajero,
                       cc.fecha, cc.total_facturas, cc.total_vendido,
                       cc.tiempo_activo_segundos, cc.created_at
                FROM {DbNames.CuadreCaja} cc
                JOIN {DbNames.Usuario} u ON u.id = cc.usuario_id
                ORDER BY cc.fecha DESC, cajero;
                """, ct),
            // Sin password_hash: jamás sale de la base de datos
            await LeerAsync(conexion, "Usuarios", $"""
                SELECT u.id, u.username, u.nombre, u.apellido, r.nombre AS rol,
                       u.activo, u.created_at, u.last_login_at
                FROM {DbNames.Usuario} u
                LEFT JOIN {DbNames.Rol} r ON r.id = u.rol_id
                ORDER BY u.id;
                """, ct),
            await LeerAsync(conexion, "Auditoría", $"""
                SELECT a.id, TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS usuario,
                       a.entidad, a.entidad_id, a.accion, a.descripcion, a.timestamp
                FROM {DbNames.Auditoria} a
                JOIN {DbNames.Usuario} u ON u.id = a.usuario_id
                ORDER BY a.id;
                """, ct)
        ];
    }

    private static async Task<TablaExportada> LeerAsync(MySqlConnection conexion, string nombre,
        string sql, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync(ct);

        var encabezados = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
            encabezados.Add(reader.GetName(i));

        var filas = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var celdas = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                celdas[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            filas.Add(celdas);
        }

        return new TablaExportada(nombre, encabezados, filas);
    }
}
