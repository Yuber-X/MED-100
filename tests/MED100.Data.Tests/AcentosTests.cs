using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Los nombres dominicanos llevan tildes y ñ (Sofía, Peña, Muñoz). Este test
/// fija que sobrevivan el viaje completo app → MySQL → app sin convertirse
/// en símbolos (mojibake).
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class AcentosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_acentos_test;";

    private UsuarioService _usuarios = null!;
    private ClienteService _clientes = null!;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await using var cmd = conexion.CreateCommand();
            cmd.CommandText = "DROP DATABASE IF EXISTS med100_acentos_test;";
            await cmd.ExecuteNonQueryAsync();
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        var factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(factory));
        var repo = new UsuarioRepository(factory);
        _usuarios = new UsuarioService(repo, auditoria);
        _clientes = new ClienteService(new ClienteRepository(factory), new HistorialRepository(factory), auditoria);

        var auth = new AuthService(repo, new SesionRepository(factory), auditoria);
        await auth.CrearCuentaInicialAsync("admin", "Admin", "clave-admin-123");
        await auth.LoginAsync("admin", "clave-admin-123");
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Usuario_ConTildesYEnie_SeGuardaYSeLeeIgual()
    {
        var roles = await _usuarios.ObtenerRolesAsync();
        var cajero = roles.First(r => r.Nombre == "Cajero").Id;

        var id = await _usuarios.CrearAsync("sofia", "Sofía", "Peña Muñoz", cajero, "clave-sofia-1");

        var guardado = (await _usuarios.ObtenerTodosAsync()).First(u => u.Id == id);
        guardado.Nombre.Should().Be("Sofía");
        guardado.Apellido.Should().Be("Peña Muñoz");
        guardado.NombreCompleto.Should().Be("Sofía Peña Muñoz");
    }

    /// <summary>
    /// El catálogo de permisos se siembra desde 002_seed_data.sql. Si el script
    /// se ejecuta con una codificación equivocada, "Almacén" y "Configuración"
    /// quedan como basura y así se ven en las casillas de Usuarios (le pasó a
    /// Yuber el 2026-07-12). Este test lo detecta.
    /// </summary>
    [Fact]
    public async Task CatalogoDePermisos_SeSiembraConLosAcentosCorrectos()
    {
        var permisos = await _usuarios.ObtenerCatalogoPermisosAsync();

        permisos.Should().Contain(p => p.Codigo == "almacen" && p.Nombre == "Almacén");
        permisos.Should().Contain(p => p.Codigo == "configuracion" && p.Nombre == "Configuración");
        permisos.Should().Contain(p => p.Codigo == "caducidad" &&
                                       p.Descripcion!.Contains("Semáforo"));
        // Nada de mojibake: los bytes UTF-8 mal interpretados producen Ã, Â, ├, ®…
        permisos.Should().OnlyContain(p =>
            !p.Nombre.Contains('Ã') && !p.Nombre.Contains('Â') && !p.Nombre.Contains('├'));
    }

    [Fact]
    public async Task Cliente_ConAcentos_SeGuardaYSeLeeIgual()
    {
        var id = await _clientes.CrearAsync(
            new MED100.Models.ClienteDatos(null, "José Ramón Núñez", null, "Av. España", "Ñoño"));

        var guardado = await _clientes.ObtenerPorIdAsync(id);
        guardado!.Nombre.Should().Be("José Ramón Núñez");
        guardado.Direccion.Should().Be("Av. España");
        guardado.Notas.Should().Be("Ñoño");
    }
}
