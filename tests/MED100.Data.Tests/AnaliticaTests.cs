using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Analítica contra MySQL real: los totales excluyen anuladas, el ranking de
/// productos y cajeros ordena bien, y el permiso 'reportes'/'panel' se exige.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class AnaliticaTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_analitica_test;";

    private VentaService _ventas = null!;
    private FacturaService _facturas = null!;
    private AnaliticaService _analitica = null!;
    private long _adminId, _cajeroId, _productoAId, _productoBId;

    private static readonly string[] PermisosAdmin =
        ["vender", "panel", "reportes", "facturas_anular", "comprobantes", "comprobantes_todos"];

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_analitica_test;");
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
        var facturaRepo = new FacturaRepository(factory);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id) VALUES
                  ('admin',  'hash', 'Ana Admin',   (SELECT id FROM rol WHERE nombre='Admin')),
                  ('cajero', 'hash', 'Carlos Caja', (SELECT id FROM rol WHERE nombre='Cajero'));
                """);
            _adminId = await Escalar(conexion, "SELECT id FROM usuario WHERE username='admin';");
            _cajeroId = await Escalar(conexion, "SELECT id FROM usuario WHERE username='cajero';");

            await Ejecutar(conexion, """
                INSERT INTO producto (codigo, nombre, precio, cantidad) VALUES
                  ('A', 'Producto A', 100.00, 100),
                  ('B', 'Producto B',  50.00, 100);
                """);
            _productoAId = await Escalar(conexion, "SELECT id FROM producto WHERE codigo='A';");
            _productoBId = await Escalar(conexion, "SELECT id FROM producto WHERE codigo='B';");
        }

        await config.CargarAsync();
        _ventas = new VentaService(facturaRepo, new ClienteRepository(factory),
            new MedicoRepository(factory), new ArsRepository(factory), config, auditoria,
            new NcfService(new NcfRepository(factory), auditoria));
        _facturas = new FacturaService(facturaRepo, auditoria);
        _analitica = new AnaliticaService(new AnaliticaRepository(factory), new AjustesLocales());

        SesionActual.Iniciar(_adminId, "admin", "Ana Admin", "Admin", PermisosAdmin, DateTime.UtcNow, 1);
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    private Task<VentaResultado> VenderAsync(long productoId, int cantidad, decimal precio,
        MetodoPagoFactura metodo = MetodoPagoFactura.Efectivo) =>
        _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(productoId, "P", cantidad, precio)],
            null, metodo, metodo == MetodoPagoFactura.Efectivo ? 100_000m : null));

    [Fact]
    public async Task Reporte_TotalesYMetodos_ExcluyenAnuladas()
    {
        await VenderAsync(_productoAId, 2, 100.00m, MetodoPagoFactura.Efectivo);   // 236.00
        await VenderAsync(_productoBId, 1, 50.00m, MetodoPagoFactura.Tarjeta);     //  59.00
        var anulada = await VenderAsync(_productoAId, 1, 100.00m);                 // 118.00
        await _facturas.AnularAsync(anulada.FacturaId, "Prueba");

        var hoy = FechaNegocio.Hoy;
        var reporte = await _analitica.ObtenerReporteAsync(hoy, hoy);

        reporte.TotalFacturas.Should().Be(2);                 // la anulada no cuenta
        reporte.TotalVendido.Should().Be(295.00m);            // 236 + 59
        reporte.PorMetodo.Efectivo.Should().Be(236.00m);
        reporte.PorMetodo.Tarjeta.Should().Be(59.00m);
        reporte.TotalItbis.Should().Be(45.00m);               // 36 + 9
        reporte.TicketPromedio.Should().Be(147.50m);          // 295 / 2
        reporte.FacturasAnuladas.Should().Be(1);
        reporte.MontoAnulado.Should().Be(118.00m);            // informado, no sumado
    }

    [Fact]
    public async Task Reporte_TopProductos_OrdenaPorUnidadesVendidas()
    {
        await VenderAsync(_productoAId, 2, 100.00m);
        await VenderAsync(_productoBId, 7, 50.00m);   // B vende más unidades
        await VenderAsync(_productoAId, 1, 100.00m);

        var hoy = FechaNegocio.Hoy;
        var reporte = await _analitica.ObtenerReporteAsync(hoy, hoy);

        reporte.TopProductos.Should().HaveCount(2);
        reporte.TopProductos[0].Nombre.Should().Be("Producto B");
        reporte.TopProductos[0].Unidades.Should().Be(7);
        reporte.TopProductos[1].Nombre.Should().Be("Producto A");
        reporte.TopProductos[1].Unidades.Should().Be(3);   // 2 + 1 agrupadas
    }

    [Fact]
    public async Task Reporte_PorCajero_AgrupaCadaVendedor()
    {
        await VenderAsync(_productoAId, 1, 100.00m);   // Ana

        SesionActual.Iniciar(_cajeroId, "cajero", "Carlos Caja", "Cajero",
            ["vender", "panel", "reportes"], DateTime.UtcNow, 2);
        await VenderAsync(_productoAId, 3, 100.00m);   // Carlos vende más

        var hoy = FechaNegocio.Hoy;
        var reporte = await _analitica.ObtenerReporteAsync(hoy, hoy);

        reporte.PorCajero.Should().HaveCount(2);
        reporte.PorCajero[0].Nombre.Should().Be("Carlos Caja");   // ordenado por total
        reporte.PorCajero[0].Total.Should().Be(354.00m);
        reporte.PorCajero[1].Nombre.Should().Be("Ana Admin");
    }

    [Fact]
    public async Task Reporte_RangoSinVentas_DevuelveCeros()
    {
        await VenderAsync(_productoAId, 1, 100.00m);

        // Una semana antes: no hay nada
        var ayer = FechaNegocio.Hoy.AddDays(-7);
        var reporte = await _analitica.ObtenerReporteAsync(ayer, ayer);

        reporte.TotalVendido.Should().Be(0m);
        reporte.TotalFacturas.Should().Be(0);
        reporte.TicketPromedio.Should().Be(0m);   // sin dividir entre cero
        reporte.VentasPorDia.Should().BeEmpty();
    }

    [Fact]
    public async Task Reporte_FechasInvertidas_Falla()
    {
        var hoy = FechaNegocio.Hoy;
        var accion = () => _analitica.ObtenerReporteAsync(hoy, hoy.AddDays(-3));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*anterior a la inicial*");
    }

    [Fact]
    public async Task Dashboard_VentasDelDiaYAlertasDeInventario()
    {
        await VenderAsync(_productoAId, 2, 100.00m);   // 236.00
        await VenderAsync(_productoBId, 1, 50.00m);    //  59.00

        var datos = await _analitica.ObtenerDashboardAsync();

        datos.VentasHoy.Should().Be(295.00m);
        datos.FacturasHoy.Should().Be(2);
        datos.VentasMes.Should().Be(295.00m);
        datos.TicketPromedioMes.Should().Be(147.50m);
        datos.VentasPorDia.Should().ContainSingle()
            .Which.Fecha.Should().Be(FechaNegocio.Hoy);
        datos.TopVendedores.Should().ContainSingle()
            .Which.Nombre.Should().Be("Ana Admin");
        // Con stock 98/99 y umbral 10, no hay alertas de stock bajo
        datos.ProductosStockBajo.Should().Be(0);
    }

    [Fact]
    public async Task Dashboard_SinPermisoPanel_Falla()
    {
        SesionActual.Iniciar(_cajeroId, "cajero", "Carlos", "Cajero", ["vender"], DateTime.UtcNow, 2);

        var accion = () => _analitica.ObtenerDashboardAsync();

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tienes permiso para ver el panel*");
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
