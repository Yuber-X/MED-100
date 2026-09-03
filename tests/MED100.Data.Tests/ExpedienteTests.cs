using System.IO.Compression;
using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// El expediente digital del paciente.
///
/// Lo delicado acá es que hay DOS cosas que tienen que quedar de acuerdo: la
/// ficha en la base y el archivo en el disco. Si una se guarda y la otra no,
/// queda una fila apuntando a la nada (o un archivo huérfano ocupando espacio
/// que nadie va a encontrar nunca).
///
/// Y la lista blanca de extensiones no es cosmética: el documento se abre con
/// doble clic y UseShellExecute, así que un .exe disfrazado de "cédula" sería
/// una puerta de entrada al equipo de la clínica.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class ExpedienteTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_expediente_test;";

    private ConexionFactory _factory = null!;
    private ExpedienteService _expedientes = null!;
    private DocumentoPacienteRepository _repo = null!;
    private string _carpetaRaiz = null!;
    private string _carpetaOrigen = null!;

    private long _usuarioId;
    private long _pacienteId;
    private long _otroPacienteId;

    private static readonly string[] PermisosCompletos =
        ["expedientes", "clientes", "clientes_editar", "usuarios", "vender", "citas", "turnos"];

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_expediente_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        _repo = new DocumentoPacienteRepository(_factory);

        // Carpetas propias por corrida: no se pisan con las de la app ni entre
        // ejecuciones que quedaron a medias.
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        _carpetaRaiz = Path.Combine(Path.GetTempPath(), $"med100_exp_{sufijo}");
        _carpetaOrigen = Path.Combine(Path.GetTempPath(), $"med100_orig_{sufijo}");
        Directory.CreateDirectory(_carpetaRaiz);
        Directory.CreateDirectory(_carpetaOrigen);

        var ajustes = new AjustesLocales { CarpetaExpedientes = _carpetaRaiz };
        _expedientes = new ExpedienteService(_repo, new ClienteRepository(_factory),
            new AuditoriaService(new AuditoriaRepository(_factory)), ajustes);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, apellido, rol_id)
                VALUES ('test', 'hash', 'Ana', 'Recepción', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            _pacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre, cedula) VALUES ('María Pérez', '001-1111111-1');
                """);
            _otroPacienteId = await InsertarAsync(conexion, """
                INSERT INTO cliente (nombre, cedula) VALUES ('Juan Otro', '001-2222222-2');
                """);
        }

        SesionActual.Iniciar(_usuarioId, "test", "Ana Recepción", "Admin",
            PermisosCompletos, DateTime.UtcNow, 1);
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
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_expediente_test;");
    }

    /// <summary>Crea un archivo de prueba y devuelve su ruta.</summary>
    private string CrearArchivo(string nombre, int bytes = 64)
    {
        var ruta = Path.Combine(_carpetaOrigen, nombre);
        File.WriteAllBytes(ruta, new byte[bytes]);
        return ruta;
    }

    // =========================================================
    // Alta: la ficha y el archivo tienen que quedar de acuerdo
    // =========================================================

    [Fact]
    public async Task Agregar_GuardaLaFichaYCopiaElArchivo()
    {
        var origen = CrearArchivo("cedula.pdf");

        var documento = await _expedientes.AgregarAsync(_pacienteId, origen,
            TipoDocumentoPaciente.Identificacion);

        documento.Nombre.Should().Be("cedula.pdf");
        documento.Tipo.Should().Be(TipoDocumentoPaciente.Identificacion);
        documento.ClienteId.Should().Be(_pacienteId);
        File.Exists(_expedientes.RutaAbsoluta(documento)).Should().BeTrue(
            "sin el archivo en disco la ficha no sirve de nada");
    }

    [Fact]
    public async Task Agregar_NoMueveNiBorraElOriginalDelUsuario()
    {
        // Si el expediente se corrompe, su archivo tiene que seguir donde estaba.
        var origen = CrearArchivo("carne-ars.jpg");

        await _expedientes.AgregarAsync(_pacienteId, origen, TipoDocumentoPaciente.Seguro);

        File.Exists(origen).Should().BeTrue();
    }

    [Fact]
    public async Task Agregar_DosArchivosConElMismoNombre_NoSePisan()
    {
        // Las dos caras de la cédula suelen llamarse igual. El id delante del
        // nombre es lo que evita que la segunda borre a la primera.
        var primero = CrearArchivo("foto.jpg");
        var a = await _expedientes.AgregarAsync(_pacienteId, primero);
        var b = await _expedientes.AgregarAsync(_pacienteId, primero);

        a.RutaRelativa.Should().NotBe(b.RutaRelativa);
        File.Exists(_expedientes.RutaAbsoluta(a)).Should().BeTrue();
        File.Exists(_expedientes.RutaAbsoluta(b)).Should().BeTrue();
        (await _expedientes.ObtenerAsync(_pacienteId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Agregar_CadaPacienteEnSuCarpeta()
    {
        var origen = CrearArchivo("papel.pdf");

        var deMaria = await _expedientes.AgregarAsync(_pacienteId, origen);
        var deJuan = await _expedientes.AgregarAsync(_otroPacienteId, origen);

        deMaria.RutaRelativa.Should().StartWith($"pacientes/{_pacienteId}/");
        deJuan.RutaRelativa.Should().StartWith($"pacientes/{_otroPacienteId}/");
    }

    [Fact]
    public async Task Agregar_DeUnPacienteQueNoExiste_SeNiega()
    {
        var origen = CrearArchivo("papel.pdf");

        var accion = () => _expedientes.AgregarAsync(999999, origen);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no existe*");
    }

    [Fact]
    public async Task Agregar_ArchivoQueNoEsta_SeNiegaYNoDejaFichaHuerfana()
    {
        var accion = () => _expedientes.AgregarAsync(_pacienteId,
            Path.Combine(_carpetaOrigen, "no-existe.pdf"));

        await accion.Should().ThrowAsync<InvalidOperationException>();
        (await _expedientes.ObtenerAsync(_pacienteId)).Should().BeEmpty(
            "no puede quedar una fila apuntando a un archivo que nunca existió");
    }

    // =========================================================
    // Lista blanca: lo que de verdad protege el equipo
    // =========================================================

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.bat")]
    [InlineData("cosa.ps1")]
    [InlineData("acceso.lnk")]
    [InlineData("libreria.dll")]
    public async Task Agregar_EjecutableDisfrazado_SeNiega(string nombre)
    {
        // El expediente se abre con doble clic y UseShellExecute: sin esta
        // puerta, "cedula.exe" sería una puerta de entrada al equipo.
        var origen = CrearArchivo(nombre);

        var accion = () => _expedientes.AgregarAsync(_pacienteId, origen);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no se permite*");
    }

    [Theory]
    [InlineData("cedula.pdf")]
    [InlineData("foto.JPG")]           // la extensión llega en mayúsculas
    [InlineData("estudio.heic")]       // iPhone
    [InlineData("consentimiento.docx")]
    [InlineData("resultados.xlsx")]
    [InlineData("todo.zip")]
    public async Task Agregar_LoQueSiSeGuardaEnUnaClinica_Entra(string nombre)
    {
        var origen = CrearArchivo(nombre);

        var documento = await _expedientes.AgregarAsync(_pacienteId, origen);

        documento.Nombre.Should().Be(nombre);
    }

    [Fact]
    public async Task Agregar_ArchivoDemasiadoGrande_SeNiegaConMensajeUtil()
    {
        var origen = CrearArchivo("enorme.pdf",
            (int)ExpedienteService.MaxBytesPorArchivo + 1024);

        var accion = () => _expedientes.AgregarAsync(_pacienteId, origen);

        (await accion.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*máximo*");
    }

    // =========================================================
    // Eliminar y re-ubicar: solo el Admin
    // =========================================================

    [Fact]
    public async Task Eliminar_SacaElDocumentoDeLaListaPeroDejaElArchivo()
    {
        // El soft delete tiene que ser reversible por soporte: borrar el
        // archivo del disco lo haría definitivo.
        var documento = await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("papel.pdf"));
        var ruta = _expedientes.RutaAbsoluta(documento);

        await _expedientes.EliminarAsync(documento.Id);

        (await _expedientes.ObtenerAsync(_pacienteId)).Should().BeEmpty();
        File.Exists(ruta).Should().BeTrue("el archivo se conserva por si hay que deshacer");
    }

    [Fact]
    public async Task Eliminar_SinSerAdmin_SeNiega()
    {
        var documento = await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("papel.pdf"));

        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "recep", "Recepción", "Servicio",
            ["expedientes", "clientes"], DateTime.UtcNow, 2);

        var accion = () => _expedientes.EliminarAsync(documento.Id);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*administrador*");
    }

    [Fact]
    public async Task Mover_LlevaLaFichaYElArchivoAlOtroPaciente()
    {
        var documento = await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("confundido.pdf"));
        var rutaVieja = _expedientes.RutaAbsoluta(documento);

        await _expedientes.MoverAsync(documento.Id, _otroPacienteId);

        (await _expedientes.ObtenerAsync(_pacienteId)).Should().BeEmpty();
        var movido = (await _expedientes.ObtenerAsync(_otroPacienteId)).Should().ContainSingle().Subject;
        movido.RutaRelativa.Should().StartWith($"pacientes/{_otroPacienteId}/");
        File.Exists(_expedientes.RutaAbsoluta(movido)).Should().BeTrue();
        File.Exists(rutaVieja).Should().BeFalse("el archivo se movió, no se copió");
    }

    [Fact]
    public async Task Mover_SinSerAdmin_SeNiega()
    {
        var documento = await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("papel.pdf"));

        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "recep", "Recepción", "Servicio",
            ["expedientes", "clientes"], DateTime.UtcNow, 2);

        var accion = () => _expedientes.MoverAsync(documento.Id, _otroPacienteId);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task SinPermisoDeExpedientes_NiSiquieraSeVe()
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "cajero", "Cajero", "Cajero",
            ["vender", "clientes"], DateTime.UtcNow, 3);

        var accion = () => _expedientes.ObtenerAsync(_pacienteId);

        await accion.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*permiso*");
    }

    // =========================================================
    // ZIP
    // =========================================================

    [Fact]
    public async Task ExportarZip_MeteTodosLosDocumentosConSuNombreReal()
    {
        await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("cedula.pdf"));
        await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("carne.jpg"));
        var zip = Path.Combine(_carpetaOrigen, "expediente.zip");

        var cantidad = await _expedientes.ExportarZipAsync(_pacienteId, zip);

        cantidad.Should().Be(2);
        using var archivo = ZipFile.OpenRead(zip);
        archivo.Entries.Select(e => e.Name).Should().BeEquivalentTo(["cedula.pdf", "carne.jpg"]);
    }

    [Fact]
    public async Task ExportarZip_ConNombresRepetidos_LosNumeraEnVezDePisarlos()
    {
        var origen = CrearArchivo("foto.jpg");
        await _expedientes.AgregarAsync(_pacienteId, origen);
        await _expedientes.AgregarAsync(_pacienteId, origen);
        var zip = Path.Combine(_carpetaOrigen, "expediente.zip");

        var cantidad = await _expedientes.ExportarZipAsync(_pacienteId, zip);

        cantidad.Should().Be(2);
        using var archivo = ZipFile.OpenRead(zip);
        archivo.Entries.Should().HaveCount(2, "el segundo no puede pisar al primero");
    }

    [Fact]
    public async Task ExportarZip_SinDocumentos_AvisaEnVezDeDejarUnZipVacio()
    {
        var zip = Path.Combine(_carpetaOrigen, "vacio.zip");

        var accion = () => _expedientes.ExportarZipAsync(_pacienteId, zip);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*todavía no tiene documentos*");
        File.Exists(zip).Should().BeFalse();
    }

    // =========================================================
    // El tablero del almacén
    // =========================================================

    [Fact]
    public async Task Resumen_CuentaLosDocumentosDeCadaPaciente()
    {
        await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("uno.pdf"));
        await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("dos.pdf"));

        var resumen = await _expedientes.ObtenerResumenAsync();

        resumen.Should().HaveCount(2, "los dos pacientes aparecen, tengan papeles o no");
        resumen.Single(r => r.ClienteId == _pacienteId).Documentos.Should().Be(2);
        resumen.Single(r => r.ClienteId == _otroPacienteId).Documentos.Should().Be(0);
    }

    [Fact]
    public async Task Resumen_PacienteQueNuncaVino_NoTraeFechaInventada()
    {
        // La consulta usa GREATEST con un centinela; si se escapa, la pantalla
        // mostraría el año 1000 como "última visita".
        var resumen = await _expedientes.ObtenerResumenAsync();

        resumen.Single(r => r.ClienteId == _otroPacienteId).UltimaVisitaUtc.Should().BeNull();
    }

    // ---- Fecha retroactiva de consulta (pedido de la clínica 2026-08-27) ----
    //
    // "Se le puede poner la fecha retroactiva de consulta de manera que el
    //  sistema arroje... todos los que tienen 6 meses sin venir a la clínica"
    //
    // Sin esto, un paciente cargado al pasar los archivos viejos figura como
    // "nunca vino" y queda FUERA del aviso — que es justo a quien hay que
    // llamar. El aviso no serviría hasta dentro de 6 meses de uso del sistema.

    [Fact]
    public async Task Resumen_LaUltimaVisitaCargadaAManoCuentaComoVisita()
    {
        var hace8Meses = FechaNegocio.Hoy.AddMonths(-8);
        await PonerVisitaPreviaAsync(_otroPacienteId, hace8Meses);

        var resumen = await _expedientes.ObtenerResumenAsync();

        var fila = resumen.Single(r => r.ClienteId == _otroPacienteId);
        fila.UltimaVisitaUtc.Should().NotBeNull();
        DateOnly.FromDateTime(fila.UltimaVisitaUtc!.Value).Should().Be(hace8Meses);
    }

    /// <summary>
    /// Y con eso el paciente entra en la lista de a quién llamar, que es todo
    /// el punto del pedido.
    /// </summary>
    [Fact]
    public async Task Resumen_ConLaFechaCargadaElPacienteApareceComoInactivo()
    {
        await PonerVisitaPreviaAsync(_otroPacienteId, FechaNegocio.Hoy.AddMonths(-8));

        var resumen = await _expedientes.ObtenerResumenAsync();
        var fila = resumen.Single(r => r.ClienteId == _otroPacienteId);

        CalculadoraInactividad.DejoDeVenir(fila.UltimaVisitaUtc, DateTime.UtcNow, 6)
            .Should().BeTrue();
    }

    /// <summary>
    /// La cargada a mano es un PISO, no la verdad: si el paciente ya tiene
    /// actividad real en el sistema y es más reciente, gana la real. Así no hay
    /// que borrar la vieja cuando el paciente vuelve.
    /// </summary>
    [Fact]
    public async Task Resumen_LaActividadRealMasRecienteLeGanaALaCargadaAMano()
    {
        await PonerVisitaPreviaAsync(_pacienteId, FechaNegocio.Hoy.AddMonths(-8));

        var ayer = DateTime.UtcNow.AddDays(-1);
        var factura =
            "INSERT INTO factura (numero_factura, cliente_id, usuario_id, fecha_emision, " +
            "subtotal, itbis_tasa, itbis, total, metodo_pago) VALUES (" +
            $"'F-VIS-1', {_pacienteId}, {_usuarioId}, '{ayer:yyyy-MM-dd HH:mm:ss}', " +
            "1000.00, 0.00, 0.00, 1000.00, 'efectivo');";

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, factura);
        }

        var resumen = await _expedientes.ObtenerResumenAsync();
        var fila = resumen.Single(r => r.ClienteId == _pacienteId);

        fila.UltimaVisitaUtc!.Value.Date.Should().Be(ayer.Date);
        CalculadoraInactividad.DejoDeVenir(fila.UltimaVisitaUtc, DateTime.UtcNow, 6)
            .Should().BeFalse("vino ayer: no es un paciente perdido");
    }

    private async Task PonerVisitaPreviaAsync(long clienteId, DateOnly fecha)
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await Ejecutar(conexion,
            $"UPDATE cliente SET ultima_visita_previa = '{fecha:yyyy-MM-dd}' WHERE id = {clienteId};");
    }

    [Fact]
    public async Task Resumen_NoCuentaLosDocumentosEliminados()
    {
        var documento = await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("papel.pdf"));
        await _expedientes.EliminarAsync(documento.Id);

        var resumen = await _expedientes.ObtenerResumenAsync();

        resumen.Single(r => r.ClienteId == _pacienteId).Documentos.Should().Be(0);
    }

    // =========================================================
    // Auditoría: lo exige la Ley 172-13
    // =========================================================

    [Fact]
    public async Task CadaAltaYCadaAperturaQuedanEnAuditoria()
    {
        var documento = await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("estudio.pdf"),
            TipoDocumentoPaciente.Estudio);
        await _expedientes.RutaParaAbrirAsync(documento);

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT accion, COUNT(*) FROM auditoria
            WHERE entidad = 'documento_paciente' GROUP BY accion ORDER BY accion;
            """;

        var acciones = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            acciones.Add(reader.GetString(0));

        acciones.Should().Contain("crear");
        acciones.Should().Contain("consultar",
            "son datos de salud: la ley obliga a poder decir quién los miró");
    }

    [Fact]
    public async Task ElDocumentoGuardaQuienLoSubio()
    {
        var documento = await _expedientes.AgregarAsync(_pacienteId, CrearArchivo("papel.pdf"));

        var leido = await _repo.ObtenerPorIdAsync(documento.Id);

        leido!.SubidoPor.Should().Be("Ana Recepción");
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
