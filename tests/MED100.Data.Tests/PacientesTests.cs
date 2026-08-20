using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Pacientes y procedencias contra MySQL real.
///
/// Lo que se prueba de verdad: que los campos nuevos (correo, nacimiento, sexo,
/// referidor) sobrevivan la ida y vuelta a la base, y que resolver la
/// procedencia NO duplique al mismo médico escrito de tres maneras. Un
/// catálogo duplicado no rompe nada visible: solo hace que el reporte de
/// "¿quién nos manda pacientes?" mienta.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class PacientesTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_pacientes_test;";

    private ConexionFactory _factory = null!;
    private ClienteService _pacientes = null!;
    private ReferidorService _referidores = null!;
    private long _usuarioId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_pacientes_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        _pacientes = new ClienteService(new ClienteRepository(_factory), new HistorialRepository(_factory), auditoria);
        _referidores = new ReferidorService(new ReferidorRepository(_factory), auditoria);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Usuario Test', (SELECT id FROM rol WHERE nombre='Admin'));
                SELECT LAST_INSERT_ID();
                """;
            _usuarioId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        SesionActual.Iniciar(_usuarioId, "test", "Usuario Test", "Admin",
            ["clientes", "clientes_editar"], DateTime.UtcNow, 1);
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_pacientes_test;");
    }

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    // =========================================================
    // Campos nuevos
    // =========================================================

    [Fact]
    public async Task Crear_GuardaLosCamposDeClinica()
    {
        var id = await _pacientes.CrearAsync(new ClienteDatos(
            "001-1234567-8", "María Pérez", "809-555-1234", "Calle 1", "Prefiere la tarde",
            Email: "maria@gmail.com",
            FechaNacimiento: new DateOnly(1990, 3, 15),
            Sexo: SexoPaciente.Femenino));

        var leido = await _pacientes.ObtenerPorIdAsync(id);

        leido.Should().NotBeNull();
        leido!.Email.Should().Be("maria@gmail.com");
        leido.FechaNacimiento.Should().Be(new DateOnly(1990, 3, 15));
        leido.Sexo.Should().Be(SexoPaciente.Femenino);
        leido.ReferidorId.Should().BeNull();
        leido.ReferidorNombre.Should().BeNull();
    }

    [Fact]
    public async Task Crear_SinDatosOpcionales_QuedaTodoEnNull()
    {
        // El caso real más común: llega alguien sin cédula ni correo y hay que
        // registrarlo igual. Nada de esto puede ser obligatorio.
        var id = await _pacientes.CrearAsync(new ClienteDatos(null, "Juan Sin Papeles", null, null, null));

        var leido = await _pacientes.ObtenerPorIdAsync(id);

        leido!.Email.Should().BeNull();
        leido.FechaNacimiento.Should().BeNull();
        leido.Sexo.Should().BeNull();
        leido.ReferidorId.Should().BeNull();
    }

    [Fact]
    public async Task Actualizar_PuedeBorrarLosOpcionales()
    {
        var id = await _pacientes.CrearAsync(new ClienteDatos(
            null, "Ana", null, null, null,
            Email: "ana@x.com", FechaNacimiento: new DateOnly(2000, 1, 1),
            Sexo: SexoPaciente.Femenino));

        await _pacientes.ActualizarAsync(id, new ClienteDatos(null, "Ana", null, null, null));

        var leido = await _pacientes.ObtenerPorIdAsync(id);
        leido!.Email.Should().BeNull();
        leido.FechaNacimiento.Should().BeNull();
        leido.Sexo.Should().BeNull();
    }

    [Theory]
    [InlineData(SexoPaciente.Femenino)]
    [InlineData(SexoPaciente.Masculino)]
    [InlineData(SexoPaciente.Otro)]
    public async Task Sexo_LosTresValoresSobrevivenElEnumDeMySql(SexoPaciente sexo)
    {
        var id = await _pacientes.CrearAsync(
            new ClienteDatos(null, $"Paciente {sexo}", null, null, null, Sexo: sexo));

        (await _pacientes.ObtenerPorIdAsync(id))!.Sexo.Should().Be(sexo);
    }

    // =========================================================
    // Validación
    // =========================================================

    [Fact]
    public async Task Crear_ConCorreoMalEscrito_SeNiega()
    {
        var accion = async () => await _pacientes.CrearAsync(
            new ClienteDatos(null, "Pedro", null, null, null, Email: "pedro@gmail"));

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no parece un correo válido*");
    }

    [Fact]
    public async Task Crear_ConNacimientoFuturo_SeNiega()
    {
        var accion = async () => await _pacientes.CrearAsync(new ClienteDatos(
            null, "Bebé del futuro", null, null, null,
            FechaNacimiento: FechaNegocio.Hoy.AddDays(1)));

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no puede ser futura*");
    }

    [Fact]
    public async Task Crear_ConNacimientoAbsurdo_SeNiega()
    {
        // El dedazo típico: 1925 en vez de 2025.
        var accion = async () => await _pacientes.CrearAsync(new ClienteDatos(
            null, "Matusalén", null, null, null,
            FechaNacimiento: new DateOnly(1800, 1, 1)));

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Revisá el año*");
    }

    [Fact]
    public async Task Crear_ConNacimientoDeHoy_SeAcepta()
    {
        // Un recién nacido es un paciente perfectamente normal en una clínica.
        var id = await _pacientes.CrearAsync(new ClienteDatos(
            null, "Recién nacido", null, null, null, FechaNacimiento: FechaNegocio.Hoy));

        (await _pacientes.ObtenerPorIdAsync(id))!.FechaNacimiento.Should().Be(FechaNegocio.Hoy);
    }

    // =========================================================
    // Procedencia
    // =========================================================

    [Fact]
    public async Task Resolver_NombreVacio_DevuelveNull()
    {
        // Lo más común: el paciente llegó por su cuenta.
        (await _referidores.ResolverAsync(null, TipoReferidor.Medico)).Should().BeNull();
        (await _referidores.ResolverAsync("   ", TipoReferidor.Medico)).Should().BeNull();
    }

    [Fact]
    public async Task Resolver_NombreNuevo_LoCrea()
    {
        var id = await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico);

        id.Should().NotBeNull();
        var creado = await _referidores.ObtenerPorIdAsync(id!.Value);
        creado!.Nombre.Should().Be("Dr. Ramírez");
        creado.Tipo.Should().Be(TipoReferidor.Medico);
        creado.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task Resolver_MismoNombreDosVeces_NoDuplica()
    {
        var primero = await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico);
        var segundo = await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico);

        segundo.Should().Be(primero);
        (await _referidores.ObtenerTodosAsync())
            .Count(r => r.Nombre == "Dr. Ramírez").Should().Be(1);
    }

    [Fact]
    public async Task Resolver_CambiaMayusculasYAcentos_SigueSiendoElMismo()
    {
        // La collation utf8mb4_unicode_ci hace el trabajo. Sin esto el reporte
        // mostraría "Dr. Ramírez" y "dr. ramirez" como dos fuentes distintas.
        var primero = await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico);
        var segundo = await _referidores.ResolverAsync("dr. ramirez", TipoReferidor.Medico);

        segundo.Should().Be(primero);
    }

    [Fact]
    public async Task Resolver_ConEspaciosDeMas_LosRecorta()
    {
        var primero = await _referidores.ResolverAsync("ARS Humano", TipoReferidor.Ars);
        var segundo = await _referidores.ResolverAsync("  ARS Humano  ", TipoReferidor.Ars);

        segundo.Should().Be(primero);
    }

    [Fact]
    public async Task Resolver_MismoNombreConTipoDistinto_SonDosCosas()
    {
        // "Humano" como ARS y "Humano" como campaña publicitaria son dos
        // fuentes distintas de pacientes, aunque se llamen igual.
        var comoArs = await _referidores.ResolverAsync("Humano", TipoReferidor.Ars);
        var comoPublicidad = await _referidores.ResolverAsync("Humano", TipoReferidor.Publicidad);

        comoPublicidad.Should().NotBe(comoArs);
    }

    [Fact]
    public async Task Paciente_ConReferidor_TraeElNombreEnElJoin()
    {
        var referidorId = await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico);
        var id = await _pacientes.CrearAsync(new ClienteDatos(
            null, "Luis", null, null, null, ReferidorId: referidorId));

        var leido = await _pacientes.ObtenerPorIdAsync(id);
        leido!.ReferidorId.Should().Be(referidorId);
        leido.ReferidorNombre.Should().Be("Dr. Ramírez");
    }

    [Fact]
    public async Task ObtenerTodos_TraeTambienALosQueVinieronSolos()
    {
        // El LEFT JOIN es lo que evita que los pacientes sin referidor —que son
        // la mayoría— desaparezcan de la lista.
        var referidorId = await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico);
        await _pacientes.CrearAsync(new ClienteDatos(null, "Con referidor", null, null, null,
            ReferidorId: referidorId));
        await _pacientes.CrearAsync(new ClienteDatos(null, "Vino solo", null, null, null));

        var todos = await _pacientes.ObtenerTodosAsync();

        todos.Should().HaveCount(2);
        todos.Single(c => c.Nombre == "Vino solo").ReferidorNombre.Should().BeNull();
    }

    [Fact]
    public async Task ContarPacientes_CuentaSoloALosVivos()
    {
        var referidorId = (await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico))!.Value;
        var a = await _pacientes.CrearAsync(new ClienteDatos(null, "A", null, null, null,
            ReferidorId: referidorId));
        await _pacientes.CrearAsync(new ClienteDatos(null, "B", null, null, null,
            ReferidorId: referidorId));

        (await _referidores.ContarPacientesAsync(referidorId)).Should().Be(2);

        await _pacientes.EliminarAsync(a);

        (await _referidores.ContarPacientesAsync(referidorId)).Should().Be(1);
    }

    [Fact]
    public async Task Referidor_SeDesactiva_YDejaDeSugerirse()
    {
        // Un médico que dejó de referir no se borra: se apaga. Los pacientes
        // que mandó siguen apuntándole y el histórico se mantiene.
        var id = (await _referidores.ResolverAsync("Dr. Ramírez", TipoReferidor.Medico))!.Value;

        await _referidores.ActualizarAsync(id,
            new ReferidorDatos("Dr. Ramírez", TipoReferidor.Medico, null, Activo: false));

        (await _referidores.ObtenerActivosAsync()).Should().NotContain(r => r.Id == id);
        (await _referidores.ObtenerTodosAsync()).Should().Contain(r => r.Id == id);
    }

    [Fact]
    public async Task Referidor_SinPermiso_NoSePuedeCrear()
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "cajero", "Cajero", "Cajero",
            ["clientes"], DateTime.UtcNow, 1);

        var accion = async () => await _referidores.ResolverAsync("Dr. Nuevo", TipoReferidor.Medico);

        await accion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tienes permiso*");
    }
}
