using FluentAssertions;
using MySqlConnector;
using MED100.Data;

namespace MED100.Data.Tests;

/// <summary>
/// Integración contra MySQL real del diagnóstico de arranque y del
/// auto-aprovisionamiento. La parte crítica de MED-100: el esquema tiene
/// TRIGGERS (bloques DELIMITER) y el seed de roles/permisos también debe
/// ejecutarse. Requiere MySQL80 local con credenciales Dev (root/root).
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class VerificadorBaseDatosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string BdProvision = "med100_provision_test";
    private const string CadenaProvision = CadenaServidor + $"Database={BdProvision};";

    public async Task InitializeAsync() => await BorrarBdProvisionAsync();

    public async Task DisposeAsync() => await BorrarBdProvisionAsync();

    private static async Task BorrarBdProvisionAsync()
    {
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS {BdProvision};";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public void BloquesEjecutables_SeparanTriggersYLimpianEncabezados()
    {
        var bloques = VerificadorBaseDatos.ObtenerBloquesEjecutables();

        // batch principal 001 + 2 triggers + batch 002 = 4 bloques
        bloques.Should().HaveCount(4);
        // Sin sentencias DELIMITER ni CREATE DATABASE (las menciones en
        // comentarios -- son inofensivas para el servidor)
        bloques.Should().OnlyContain(b =>
            !System.Text.RegularExpressions.Regex.IsMatch(b, @"(?m)^\s*DELIMITER\b") &&
            !System.Text.RegularExpressions.Regex.IsMatch(b, @"(?m)^\s*CREATE DATABASE\b"));
        bloques[1].Should().Contain("trg_usuario_after_insert").And.NotContain("$$");
        bloques[2].Should().Contain("trg_usuario_after_update").And.NotContain("$$");
        bloques[3].Should().Contain("INSERT INTO rol");
    }

    [Fact]
    public async Task Verificar_BaseDatosInexistente_ReportaFaltaBaseDatos()
    {
        var verificador = new VerificadorBaseDatos(CadenaProvision);

        (await verificador.VerificarAsync()).Should().Be(EstadoBaseDatos.FaltaBaseDatos);
    }

    [Fact]
    public async Task CrearEsquema_DesdeCero_DejaBaseDatosListaConRolesYTriggers()
    {
        var verificador = new VerificadorBaseDatos(CadenaProvision);

        await verificador.CrearEsquemaAsync();

        (await verificador.VerificarAsync()).Should().Be(EstadoBaseDatos.Lista);

        await using var conexion = new MySqlConnection(CadenaProvision);
        await conexion.OpenAsync();

        // Seed ejecutado: 4 roles y sus permisos
        (await Escalar(conexion, "SELECT COUNT(*) FROM rol;")).Should().Be(4);
        // Admin tiene TODOS los permisos que existan. Se compara contra el
        // catálogo en vez de contra un número fijo: así el test no hay que
        // tocarlo cada vez que se agrega un permiso, y sigue detectando que a
        // Admin se le escape alguno.
        var permisosDelCatalogo = await Escalar(conexion, "SELECT COUNT(*) FROM permiso;");
        (await Escalar(conexion, """
            SELECT COUNT(*) FROM rol_permiso rp
            JOIN rol r ON r.id = rp.rol_id WHERE r.nombre = 'Admin';
            """)).Should().Be(permisosDelCatalogo);

        // Triggers vivos: crear un usuario Cajero debe heredar los permisos de su rol
        await using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('t', 'hash', 'T', (SELECT id FROM rol WHERE nombre = 'Cajero'));
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        var permisosDelCajero = await Escalar(conexion, """
            SELECT COUNT(*) FROM rol_permiso rp
            JOIN rol r ON r.id = rp.rol_id WHERE r.nombre = 'Cajero';
            """);
        (await Escalar(conexion, "SELECT COUNT(*) FROM usuario_permiso;"))
            .Should().Be(permisosDelCajero);

        // configuracion_negocio sembrada con su única fila
        (await Escalar(conexion, "SELECT COUNT(*) FROM configuracion_negocio;")).Should().Be(1);
    }

    private static async Task<int> Escalar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
