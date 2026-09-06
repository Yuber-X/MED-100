using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Cuadre GENERAL (pedido Yuber 2026-07-12): desglose de todos los cajeros en
/// una sola vista, y cierre de todos los turnos pendientes de una vez.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class CuadreGeneralTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_cuadregral_test;";

    private VentaService _ventas = null!;
    private CuadreService _cuadres = null!;
    private long _adminId, _cajeroId, _productoId;

    private static readonly string[] PermisosAdmin =
        ["vender", "cuadre", "cuadre_todos", "comprobantes"];
    private static readonly string[] PermisosCajero = ["vender", "cuadre", "comprobantes"];

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_cuadregral_test;");
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
                "INSERT INTO producto (codigo, nombre, precio, cantidad) VALUES ('P', 'Producto', 100.00, 200);");
            _productoId = await Escalar(conexion, "SELECT id FROM producto WHERE codigo='P';");
        }

        await config.CargarAsync();
        _ventas = new VentaService(new FacturaRepository(factory), new ClienteRepository(factory),
            new MedicoRepository(factory), new ArsRepository(factory), config, auditoria,
            new NcfService(new NcfRepository(factory), auditoria));
        _cuadres = new CuadreService(new CuadreRepository(factory), auditoria);
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    private void ComoAdmin() =>
        SesionActual.Iniciar(_adminId, "admin", "Ana Admin", "Admin", PermisosAdmin, DateTime.UtcNow, 1);

    private void ComoCajero() =>
        SesionActual.Iniciar(_cajeroId, "cajero", "Carlos Caja", "Cajero", PermisosCajero, DateTime.UtcNow, 2);

    private Task VenderAsync(int cantidad, MetodoPagoFactura metodo = MetodoPagoFactura.Efectivo) =>
        _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(_productoId, "Producto", cantidad, 100.00m)],
            null, metodo, metodo == MetodoPagoFactura.Efectivo ? 100_000m : null));

    [Fact]
    public async Task General_MuestraElDesgloseDeCadaCajeroYLosTotalesDelNegocio()
    {
        ComoAdmin();
        await VenderAsync(1);                                   // Ana: 118.00 efectivo
        ComoCajero();
        await VenderAsync(2);                                   // Carlos: 236.00 efectivo
        await VenderAsync(1, MetodoPagoFactura.Tarjeta);        // Carlos: 118.00 tarjeta

        ComoAdmin();
        var general = await _cuadres.CalcularGeneralAsync(FechaNegocio.Hoy);

        general.PorCajero.Should().HaveCount(2);
        general.TotalFacturas.Should().Be(3);
        general.TotalVendido.Should().Be(472.00m);              // 118 + 236 + 118
        general.TotalEfectivo.Should().Be(354.00m);
        general.TotalTarjeta.Should().Be(118.00m);

        var carlos = general.PorCajero.First(c => c.NombreCajero == "Carlos Caja");
        carlos.TotalFacturas.Should().Be(2);
        carlos.TotalVendido.Should().Be(354.00m);
        general.HayPendientes.Should().BeTrue();
    }

    [Fact]
    public async Task CerrarPendientes_CierraTodosLosTurnosDeUnaVez()
    {
        ComoAdmin();
        await VenderAsync(1);
        ComoCajero();
        await VenderAsync(1);

        ComoAdmin();
        var cerrados = await _cuadres.CerrarPendientesDelDiaAsync(FechaNegocio.Hoy);

        cerrados.Should().Be(2);
        var general = await _cuadres.CalcularGeneralAsync(FechaNegocio.Hoy);
        general.TodosCerrados.Should().BeTrue();
        general.HayPendientes.Should().BeFalse();
    }

    [Fact]
    public async Task CerrarPendientes_DosVeces_NoDuplicaNiFalla()
    {
        ComoAdmin();
        await VenderAsync(1);

        (await _cuadres.CerrarPendientesDelDiaAsync(FechaNegocio.Hoy)).Should().Be(1);
        // La segunda pasada no encuentra pendientes (el cierre automático puede repetirse)
        (await _cuadres.CerrarPendientesDelDiaAsync(FechaNegocio.Hoy)).Should().Be(0);

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        (await Escalar(conexion, "SELECT COUNT(*) FROM cuadre_caja;")).Should().Be(1);
    }

    [Fact]
    public async Task General_UnCajeroNoPuedeVerlo()
    {
        ComoCajero();

        var accion = () => _cuadres.CalcularGeneralAsync(FechaNegocio.Hoy);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Supervisor o Administrador*");
    }

    [Fact]
    public async Task General_SinVentas_QuedaVacioYNoRompe()
    {
        ComoAdmin();

        var general = await _cuadres.CalcularGeneralAsync(FechaNegocio.Hoy.AddDays(-3));

        general.PorCajero.Should().BeEmpty();
        general.TotalVendido.Should().Be(0m);
        general.TodosCerrados.Should().BeFalse();   // sin cajeros no hay nada que cerrar
        general.HayPendientes.Should().BeFalse();
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
