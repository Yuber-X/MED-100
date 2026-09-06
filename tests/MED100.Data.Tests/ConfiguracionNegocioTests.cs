using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Integración de la configuración del negocio contra MySQL real: guardar
/// exige permiso 'configuracion' (Admin), queda auditado, y el ITBIS apagado
/// hace que la factura se emita SIN impuesto (pedidos de Yuber 2026-07-12).
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class ConfiguracionNegocioTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_config_test;";

    private ConfiguracionNegocioService _config = null!;
    private VentaService _ventas = null!;
    private long _productoId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_config_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        var factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(factory));
        _config = new ConfiguracionNegocioService(new ConfiguracionNegocioRepository(factory), auditoria);
        _ventas = new VentaService(new FacturaRepository(factory),
            new ClienteRepository(factory), new MedicoRepository(factory),
            new ArsRepository(factory), _config, auditoria,
            new NcfService(new NcfRepository(factory), auditoria));

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('admin', 'hash', 'Admin Test', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            var usuarioId = await Escalar(conexion, "SELECT id FROM usuario WHERE username='admin';");
            await Ejecutar(conexion,
                "INSERT INTO producto (codigo, nombre, precio, cantidad) VALUES ('P-1', 'Producto', 100.00, 20);");
            _productoId = await Escalar(conexion, "SELECT id FROM producto WHERE codigo='P-1';");

            SesionActual.Iniciar(usuarioId, "admin", "Admin Test", "Admin",
                ["vender", "configuracion"], DateTime.UtcNow, 1);
        }

        await _config.CargarAsync();
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Guardar_NombreYRnc_PersisteYQuedaAuditado()
    {
        var cfg = _config.Actual;
        cfg.NombreNegocio = "Farmacia La Esperanza";
        cfg.Rnc = "131-12345-6";

        await _config.GuardarAsync(cfg);

        _config.Actual.NombreNegocio.Should().Be("Farmacia La Esperanza");
        _config.Actual.Rnc.Should().Be("131-12345-6");

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        (await Escalar(conexion,
            "SELECT COUNT(*) FROM auditoria WHERE entidad='configuracion_negocio' AND accion='modificar';"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Guardar_RncVacio_SeAceptaComoNull()
    {
        var cfg = _config.Actual;
        cfg.Rnc = null;   // el RNC es opcional

        await _config.GuardarAsync(cfg);

        _config.Actual.Rnc.Should().BeNull();
    }

    [Fact]
    public async Task ItbisDesactivado_LaFacturaSeEmiteSinImpuesto()
    {
        var cfg = _config.Actual;
        cfg.ItbisActivo = false;
        await _config.GuardarAsync(cfg);

        var resultado = await _ventas.RegistrarVentaAsync(new VentaSolicitud(
            [new VentaLinea(_productoId, "Producto", 2, 100.00m)],
            ClienteId: null, MetodoPagoFactura.Efectivo, EfectivoRecibido: 200.00m));

        resultado.Totales.Itbis.Should().Be(0m);
        resultado.Totales.Total.Should().Be(200.00m);   // sin el 18%
        resultado.Cambio.Should().Be(0m);

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        (await Escalar(conexion, "SELECT COUNT(*) FROM factura WHERE itbis = 0.00 AND itbis_tasa = 0.00;"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Guardar_SinPermisoConfiguracion_Falla()
    {
        // Un Cajero no puede tocar la configuración (regla: solo Admin)
        SesionActual.Iniciar(1, "cajero", "Cajero", "Cajero", ["vender"], DateTime.UtcNow, 2);

        var accion = () => _config.GuardarAsync(_config.Actual);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Solo el Administrador*");
    }

    [Fact]
    public async Task Guardar_TasaInvalida_Falla()
    {
        var cfg = _config.Actual;
        cfg.ItbisTasa = 150m;

        var accion = () => _config.GuardarAsync(cfg);

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*entre 0 y 100*");
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
