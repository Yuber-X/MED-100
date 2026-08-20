using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// El archivado AUTOMÁTICO de documentos en el expediente (pedido 2026-08-15).
///
/// Va por un camino distinto al de subir un archivo a mano, y la diferencia no
/// es cosmética: <b>no exige el permiso <c>expedientes</c></b>. Si lo exigiera,
/// ninguna factura cobrada por un Cajero quedaría archivada —el Cajero no tiene
/// ese permiso a propósito— y el fallo sería invisible: la venta saldría bien,
/// el ticket se imprimiría, y la copia simplemente no estaría.
///
/// Ese es el caso que estos tests fijan.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class ArchivadoAutomaticoTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_archivado_test;";

    private ConexionFactory _factory = null!;
    private ExpedienteService _expedientes = null!;
    private DocumentoPacienteRepository _repo = null!;
    private string _carpetaRaiz = null!;
    private string _carpetaOrigen = null!;

    private long _usuarioId;
    private long _pacienteId;

    /// <summary>Lo que de verdad tiene un Cajero: cobra, pero NO ve expedientes.</summary>
    private static readonly string[] PermisosCajero =
        ["vender", "clientes", "clientes_editar", "comprobantes", "cuadre", "citas", "turnos"];

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_archivado_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _repo = new DocumentoPacienteRepository(_factory);

        var sufijo = Guid.NewGuid().ToString("N")[..8];
        _carpetaRaiz = Path.Combine(Path.GetTempPath(), $"med100_arch_{sufijo}");
        _carpetaOrigen = Path.Combine(Path.GetTempPath(), $"med100_archorig_{sufijo}");
        Directory.CreateDirectory(_carpetaRaiz);
        Directory.CreateDirectory(_carpetaOrigen);

        _expedientes = new ExpedienteService(_repo, new ClienteRepository(_factory),
            new AuditoriaService(new AuditoriaRepository(_factory)),
            new AjustesLocales { CarpetaExpedientes = _carpetaRaiz });

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, apellido, rol_id)
                VALUES ('caja', 'hash', 'Carlos', 'Caja', (SELECT id FROM rol WHERE nombre='Cajero'));
                """);
            _pacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre, cedula) VALUES ('María Pérez', '001-1111111-1');
                """);
        }

        SesionActual.Iniciar(_usuarioId, "caja", "Carlos Caja", "Cajero",
            PermisosCajero, DateTime.UtcNow, 1);
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        foreach (var carpeta in new[] { _carpetaRaiz, _carpetaOrigen })
        {
            try { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true); }
            catch { /* que no falle el test por un archivo trabado */ }
        }
        return LimpiarBaseAsync();
    }

    private static async Task LimpiarBaseAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_archivado_test;");
    }

    /// <summary>Simula el PDF que genera el shell antes de archivarlo.</summary>
    private string CrearPdf(string nombre)
    {
        var ruta = Path.Combine(_carpetaOrigen, nombre);
        File.WriteAllBytes(ruta, "%PDF-1.4 (de mentira, para el test)"u8.ToArray());
        return ruta;
    }

    // =========================================================
    // El caso que motiva todo esto
    // =========================================================

    [Fact]
    public async Task ElCajero_QueNoVeExpedientes_IgualArchivaSuFactura()
    {
        // Si esto falla, ninguna factura del mostrador queda guardada y nadie
        // se entera hasta que un paciente pide su copia.
        SesionActual.TienePermiso("expedientes").Should().BeFalse(
            "el Cajero no ve expedientes: es justamente la premisa del test");

        var archivado = await _expedientes.ArchivarImpresoAsync(_pacienteId,
            CrearPdf("Factura F-0001.pdf"), TipoDocumentoPaciente.Factura);

        archivado.Should().BeTrue();
        var documentos = await _repo.ObtenerDeAsync(_pacienteId);
        documentos.Should().ContainSingle().Which.Nombre.Should().Be("Factura F-0001.pdf");
    }

    [Fact]
    public async Task ElArchivoQuedaEnLaCarpetaDelPaciente()
    {
        await _expedientes.ArchivarImpresoAsync(_pacienteId, CrearPdf("Factura F-0002.pdf"),
            TipoDocumentoPaciente.Factura);

        var documento = (await _repo.ObtenerDeAsync(_pacienteId)).Single();
        documento.RutaRelativa.Should().StartWith($"pacientes/{_pacienteId}/");
        File.Exists(_expedientes.RutaAbsoluta(documento)).Should().BeTrue();
    }

    [Fact]
    public async Task ElTemporalDelShell_NoSeMueve_SeCopia()
    {
        // El shell borra el temporal después; si el servicio lo MOVIERA, ese
        // borrado explotaría con "archivo no encontrado".
        var origen = CrearPdf("Factura F-0003.pdf");

        await _expedientes.ArchivarImpresoAsync(_pacienteId, origen, TipoDocumentoPaciente.Factura);

        File.Exists(origen).Should().BeTrue();
    }

    [Fact]
    public async Task QuedaRegistradoQuienLoArchivoYCuando()
    {
        await _expedientes.ArchivarImpresoAsync(_pacienteId, CrearPdf("Factura F-0004.pdf"),
            TipoDocumentoPaciente.Factura);

        (await _repo.ObtenerDeAsync(_pacienteId)).Single().SubidoPor.Should().Be("Carlos Caja");

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM auditoria
            WHERE entidad = 'documento_paciente' AND accion = 'crear';
            """;
        Convert.ToInt64(await cmd.ExecuteScalarAsync()).Should().Be(1,
            "son datos de salud: la Ley 172-13 obliga a dejar el rastro");
    }

    // =========================================================
    // Nunca puede tumbar el cobro
    // =========================================================

    [Fact]
    public async Task SiFaltaElArchivo_DevuelveFalse_YNoRevienta()
    {
        // Corre justo después de emitir la factura: una excepción acá le
        // reventaría la pantalla al cajero con el paciente esperando el papel.
        var archivado = await _expedientes.ArchivarImpresoAsync(_pacienteId,
            Path.Combine(_carpetaOrigen, "no-existe.pdf"), TipoDocumentoPaciente.Factura);

        archivado.Should().BeFalse();
        (await _repo.ObtenerDeAsync(_pacienteId)).Should().BeEmpty(
            "no puede quedar una ficha apuntando a un archivo que no está");
    }

    [Fact]
    public async Task SiElPacienteNoExiste_DevuelveFalse_YNoRevienta()
    {
        var archivado = await _expedientes.ArchivarImpresoAsync(999999,
            CrearPdf("Factura F-0005.pdf"), TipoDocumentoPaciente.Factura);

        archivado.Should().BeFalse();
    }

    [Fact]
    public async Task SinSesionAbierta_NoArchiva()
    {
        SesionActual.Cerrar();

        var archivado = await _expedientes.ArchivarImpresoAsync(_pacienteId,
            CrearPdf("Factura F-0006.pdf"), TipoDocumentoPaciente.Factura);

        archivado.Should().BeFalse();
    }

    // =========================================================
    // No duplicar la misma factura
    // =========================================================

    [Fact]
    public async Task YaTieneDocumento_ReconoceLaFacturaYaArchivada()
    {
        await _expedientes.ArchivarImpresoAsync(_pacienteId, CrearPdf("Factura F-0007.pdf"),
            TipoDocumentoPaciente.Factura);

        // Para consultar sí hace falta el permiso: es una persona mirando.
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "admin", "Admin", "Admin",
            ["expedientes", "clientes", "usuarios"], DateTime.UtcNow, 2);

        (await _expedientes.YaTieneDocumentoAsync(_pacienteId, "Factura F-0007"))
            .Should().BeTrue();
        (await _expedientes.YaTieneDocumentoAsync(_pacienteId, "Factura F-9999"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task YaTieneDocumento_NoCuentaLosEliminados()
    {
        await _expedientes.ArchivarImpresoAsync(_pacienteId, CrearPdf("Factura F-0008.pdf"),
            TipoDocumentoPaciente.Factura);

        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "admin", "Admin", "Admin",
            ["expedientes", "clientes", "usuarios"], DateTime.UtcNow, 2);

        var documento = (await _repo.ObtenerDeAsync(_pacienteId)).Single();
        await _expedientes.EliminarAsync(documento.Id);

        (await _expedientes.YaTieneDocumentoAsync(_pacienteId, "Factura F-0008"))
            .Should().BeFalse("si se quitó a propósito, se tiene que poder volver a archivar");
    }

    // =========================================================

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertarAsync(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql + " SELECT LAST_INSERT_ID();";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
