using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Integración de la emisión de ventas contra MySQL real (BD med100_test,
/// recreada por corrida con el aprovisionamiento embebido). Casos obligatorios
/// spec §11: venta simple, stock insuficiente sin persistir nada, y emisión
/// concurrente con números distintos.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class FlujoVentaTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_test;";

    private VentaService _ventas = null!;
    private long _productoAId, _productoBId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        // MED-100 nace con el ITBIS APAGADO porque los servicios de salud
        // están exentos. Estas pruebas miden reportes y cuadre con números
        // que incluyen 18%, así que lo encienden explícitamente en vez de
        // depender de un default que ya no es el suyo.
        await using (var cfg = new MySqlConnection(CadenaTest))
        {
            await cfg.OpenAsync();
            await Ejecutar(cfg,
                "UPDATE configuracion_negocio SET itbis_activo = 1, itbis_tasa = 18.00 WHERE id = 1;");
        }

        var factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(factory));
        var config = new ConfiguracionNegocioService(new ConfiguracionNegocioRepository(factory), auditoria);

        // Usuario admin de prueba + productos con stock conocido
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            var usuarioId = await Escalar(conexion, "SELECT id FROM usuario WHERE username='test';");

            await Ejecutar(conexion, """
                INSERT INTO producto (codigo, nombre, precio, cantidad) VALUES
                  ('A-1', 'Producto A', 100.00, 10),
                  ('B-1', 'Producto B', 33.33, 5);
                """);
            _productoAId = await Escalar(conexion, "SELECT id FROM producto WHERE codigo='A-1';");
            _productoBId = await Escalar(conexion, "SELECT id FROM producto WHERE codigo='B-1';");

            SesionActual.Iniciar(usuarioId, "test", "Usuario Test", "Admin",
                ["vender", "clientes", "productos"], DateTime.UtcNow, 1);
        }

        await config.CargarAsync();
        _ventas = new VentaService(new FacturaRepository(factory),
            new ClienteRepository(factory), new MedicoRepository(factory),
            new ArsRepository(factory), config, auditoria);
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task VentaSimple_EfectivoExacto_PersisteTodoYDescuentaStock()
    {
        var resultado = await _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(_productoAId, "Producto A", 2, 100.00m)],
            ClienteId: null, MetodoPagoFactura.Efectivo, EfectivoRecibido: 236.00m));

        resultado.NumeroFactura.Should().Be("F-0001");
        resultado.Totales.Total.Should().Be(236.00m);   // 200 + 18% = 236
        resultado.Cambio.Should().Be(0m);

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        (await Escalar(conexion, "SELECT COUNT(*) FROM factura;")).Should().Be(1);
        (await Escalar(conexion, "SELECT COUNT(*) FROM detalle;")).Should().Be(1);
        (await Escalar(conexion, $"SELECT cantidad FROM producto WHERE id={_productoAId};")).Should().Be(8);
        (await Escalar(conexion, "SELECT COUNT(*) FROM auditoria WHERE entidad='factura';")).Should().Be(1);
        // cliente_id NULL = consumidor final (regla Yuber)
        (await Escalar(conexion, "SELECT COUNT(*) FROM factura WHERE cliente_id IS NULL;")).Should().Be(1);
    }

    [Fact]
    public async Task StockInsuficiente_RevierteTodo_NadaPersistido()
    {
        var accion = () => _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [
                new VentaLinea(_productoAId, "Producto A", 1, 100.00m),
                new VentaLinea(_productoBId, "Producto B", 99, 33.33m)   // solo hay 5
            ],
            null, MetodoPagoFactura.Tarjeta, null));

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Stock insuficiente*Producto B*");

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        (await Escalar(conexion, "SELECT COUNT(*) FROM factura;")).Should().Be(0);
        (await Escalar(conexion, "SELECT COUNT(*) FROM detalle;")).Should().Be(0);
        // El stock de A (línea previa al fallo) también quedó intacto
        (await Escalar(conexion, $"SELECT cantidad FROM producto WHERE id={_productoAId};")).Should().Be(10);
        // El número reservado se revirtió: la próxima factura sigue siendo la 1
        (await Escalar(conexion, "SELECT factura_siguiente FROM configuracion_negocio;")).Should().Be(1);
    }

    [Fact]
    public async Task VentasConcurrentes_NumerosDeFacturaDistintos()
    {
        var tareas = Enumerable.Range(0, 4).Select(_ =>
            _ventas.RegistrarVentaAsync(new VentaSolicitud(
                [new VentaLinea(_productoAId, "Producto A", 1, 100.00m)],
                null, MetodoPagoFactura.Tarjeta, null)));

        var resultados = await Task.WhenAll(tareas);

        resultados.Select(r => r.NumeroFactura).Should().OnlyHaveUniqueItems();
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        (await Escalar(conexion, $"SELECT cantidad FROM producto WHERE id={_productoAId};")).Should().Be(6);
        (await Escalar(conexion, "SELECT factura_siguiente FROM configuracion_negocio;")).Should().Be(5);
    }

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> Escalar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
