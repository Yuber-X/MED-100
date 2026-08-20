using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Fase 4 contra MySQL real: búsqueda de comprobantes con su alcance por rol,
/// anulación (devuelve stock, no se puede repetir, queda auditada) y cuadre de caja.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class OperacionesTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_ops_test;";

    private VentaService _ventas = null!;
    private FacturaService _facturas = null!;
    private CuadreService _cuadres = null!;
    private long _adminId, _cajeroId, _productoId;

    private static readonly string[] PermisosAdmin =
        ["vender", "comprobantes", "comprobantes_todos", "facturas_anular", "cuadre", "cuadre_todos"];
    private static readonly string[] PermisosCajero =
        ["vender", "comprobantes", "cuadre"];   // sin _todos ni anular

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_ops_test;");
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

            await Ejecutar(conexion,
                "INSERT INTO producto (codigo, nombre, precio, cantidad) VALUES ('P-1', 'Producto', 100.00, 50);");
            _productoId = await Escalar(conexion, "SELECT id FROM producto WHERE codigo='P-1';");
        }

        await config.CargarAsync();
        _ventas = new VentaService(facturaRepo, new ClienteRepository(factory),
            new MedicoRepository(factory), new ArsRepository(factory), config, auditoria);
        _facturas = new FacturaService(facturaRepo, auditoria);
        _cuadres = new CuadreService(new CuadreRepository(factory), auditoria);
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    private void IniciarComoAdmin() =>
        SesionActual.Iniciar(_adminId, "admin", "Ana Admin", "Admin", PermisosAdmin, DateTime.UtcNow, 1);

    private void IniciarComoCajero() =>
        SesionActual.Iniciar(_cajeroId, "cajero", "Carlos Caja", "Cajero", PermisosCajero, DateTime.UtcNow, 2);

    private Task<VentaResultado> VenderAsync(int cantidad = 2,
        MetodoPagoFactura metodo = MetodoPagoFactura.Efectivo) =>
        _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(_productoId, "Producto", cantidad, 100.00m)],
            null, metodo,
            metodo == MetodoPagoFactura.Efectivo ? 10_000m : null));

    // ---------------- Anulación ----------------

    [Fact]
    public async Task Anular_DevuelveStock_MarcaAnuladaYAudita()
    {
        IniciarComoAdmin();
        var venta = await VenderAsync(cantidad: 3);   // stock 50 → 47

        await _facturas.AnularAsync(venta.FacturaId, "Cliente devolvió la compra");

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        // La factura NO se borra: sigue ahí, marcada
        (await Escalar(conexion, "SELECT COUNT(*) FROM factura;")).Should().Be(1);
        (await Escalar(conexion, "SELECT COUNT(*) FROM factura WHERE estado='anulada';")).Should().Be(1);
        // El stock volvió al inventario
        (await Escalar(conexion, $"SELECT cantidad FROM producto WHERE id={_productoId};")).Should().Be(50);
        // Queda el rastro con acción 'anular' y el motivo
        (await Escalar(conexion,
            "SELECT COUNT(*) FROM auditoria WHERE accion='anular' AND descripcion LIKE '%devolvió%';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Anular_DosVeces_FallaYNoDuplicaElStock()
    {
        IniciarComoAdmin();
        var venta = await VenderAsync(cantidad: 5);   // 50 → 45
        await _facturas.AnularAsync(venta.FacturaId, "Error de digitación");

        var accion = () => _facturas.AnularAsync(venta.FacturaId, "Otra vez");

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ya estaba anulada*");

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        // El stock volvió UNA sola vez (no 55)
        (await Escalar(conexion, $"SELECT cantidad FROM producto WHERE id={_productoId};")).Should().Be(50);
    }

    [Fact]
    public async Task Anular_SinPermiso_FallaYNoTocaNada()
    {
        IniciarComoAdmin();
        var venta = await VenderAsync(cantidad: 4);   // 50 → 46

        IniciarComoCajero();   // el Cajero no tiene 'facturas_anular'
        var accion = () => _facturas.AnularAsync(venta.FacturaId, "Intento no autorizado");

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tienes permiso para anular*");

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        (await Escalar(conexion, "SELECT COUNT(*) FROM factura WHERE estado='emitida';")).Should().Be(1);
        (await Escalar(conexion, $"SELECT cantidad FROM producto WHERE id={_productoId};")).Should().Be(46);
    }

    [Fact]
    public async Task Anular_SinMotivo_Falla()
    {
        IniciarComoAdmin();
        var venta = await VenderAsync();

        var accion = () => _facturas.AnularAsync(venta.FacturaId, "   ");

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*motivo*");
    }

    // ---------------- Alcance de la búsqueda ----------------

    [Fact]
    public async Task Buscar_ElCajeroSoloVeSusPropiasFacturas()
    {
        IniciarComoAdmin();
        await VenderAsync();          // factura de Ana
        IniciarComoCajero();
        await VenderAsync();          // factura de Carlos

        // Carlos (sin 'comprobantes_todos') solo ve la suya
        var comoCajero = await _facturas.BuscarAsync(new FiltroComprobantes(null, null, null, null));
        comoCajero.Should().HaveCount(1);
        comoCajero[0].UsuarioId.Should().Be(_cajeroId);

        // Aunque pida explícitamente las de Ana, el service fuerza su alcance
        var intentoEspiar = await _facturas.BuscarAsync(
            new FiltroComprobantes(null, null, null, UsuarioId: _adminId));
        intentoEspiar.Should().OnlyContain(f => f.UsuarioId == _cajeroId);

        // Ana sí ve las dos
        IniciarComoAdmin();
        (await _facturas.BuscarAsync(new FiltroComprobantes(null, null, null, null)))
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task ObtenerCompleta_ReimpresionConservaLineasYTotales()
    {
        IniciarComoAdmin();
        var venta = await VenderAsync(cantidad: 2);   // 200 + 18% = 236

        var factura = await _facturas.ObtenerCompletaAsync(venta.FacturaId);
        factura.Should().NotBeNull();

        var paraTicket = FacturaService.AVentaResultado(factura!);
        paraTicket.NumeroFactura.Should().Be(venta.NumeroFactura);
        paraTicket.Totales.Total.Should().Be(236.00m);
        paraTicket.Lineas.Should().ContainSingle()
            .Which.Cantidad.Should().Be(2);
        paraTicket.NombreCliente.Should().BeNull();   // consumidor final
    }

    // ---------------- Cuadre de caja ----------------

    [Fact]
    public async Task Cuadre_SumaPorMetodoDePago_ExcluyeAnuladas()
    {
        IniciarComoAdmin();
        await VenderAsync(cantidad: 1, MetodoPagoFactura.Efectivo);       // 118.00
        await VenderAsync(cantidad: 1, MetodoPagoFactura.Tarjeta);        // 118.00
        var anulada = await VenderAsync(cantidad: 1, MetodoPagoFactura.Efectivo);
        await _facturas.AnularAsync(anulada.FacturaId, "Prueba de anulación");

        var cuadre = await _cuadres.CalcularAsync(_adminId, FechaNegocio.Hoy);

        cuadre.TotalFacturas.Should().Be(2);              // la anulada no cuenta
        cuadre.TotalVendido.Should().Be(236.00m);
        cuadre.TotalEfectivo.Should().Be(118.00m);
        cuadre.TotalTarjeta.Should().Be(118.00m);
        cuadre.FacturasAnuladas.Should().Be(1);
        cuadre.MontoAnulado.Should().Be(118.00m);         // se informa aparte
        cuadre.YaCerrado.Should().BeFalse();
    }

    [Fact]
    public async Task Cuadre_CerrarDosVeces_Falla()
    {
        IniciarComoAdmin();
        await VenderAsync();

        var cuadre = await _cuadres.CalcularAsync(_adminId, FechaNegocio.Hoy);
        await _cuadres.CerrarAsync(cuadre);

        // Recalculado ya viene marcado como cerrado (inmutable, spec §9.8)
        var recalculado = await _cuadres.CalcularAsync(_adminId, FechaNegocio.Hoy);
        recalculado.YaCerrado.Should().BeTrue();

        var accion = () => _cuadres.CerrarAsync(recalculado);
        await accion.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ya fue cerrado*");
    }

    [Fact]
    public async Task Cuadre_ElCajeroNoPuedeVerElDeOtro()
    {
        IniciarComoCajero();

        var accion = () => _cuadres.CalcularAsync(_adminId, FechaNegocio.Hoy);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tu propio turno*");
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
