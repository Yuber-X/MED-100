using FluentAssertions;
using MySqlConnector;
using MED100.Data;

namespace MED100.Data.Tests;

/// <summary>
/// La tabla <c>licencia</c> contra MySQL real: la fila se crea sola, la fecha
/// de instalación solo se mueve hacia atrás, la última apertura nunca retrocede
/// (es la prueba del reloj movido) y activar dos veces no pisa la primera.
///
/// También comprueba lo que evita que la actualización rompa las instalaciones
/// que ya existen: <see cref="VerificadorBaseDatos.ActualizarEsquemaAsync"/>
/// sobre una base sin la tabla.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class LicenciaTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_licencia_test;";

    private static readonly DateTime Instalacion = new(2026, 8, 18, 15, 0, 0, DateTimeKind.Utc);

    private LicenciaRepository _repo = null!;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_licencia_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _repo = new LicenciaRepository(new ConexionFactory(CadenaTest));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ObtenerAsync_CreaLaFilaLaPrimeraVez()
    {
        var licencia = await _repo.ObtenerAsync(Instalacion);

        licencia.InstaladaAtUtc.Should().Be(Instalacion);
        licencia.InstaladaAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        licencia.UltimaAperturaUtc.Should().Be(Instalacion);
        licencia.Activada.Should().BeFalse();
        licencia.ActivadaAtUtc.Should().BeNull();
        licencia.ActivadaPor.Should().BeNull();
    }

    [Fact]
    public async Task ObtenerAsync_NoPisaLaFechaDeInstalacionEnArranquesPosteriores()
    {
        await _repo.ObtenerAsync(Instalacion);

        var diezDiasDespues = await _repo.ObtenerAsync(Instalacion.AddDays(10));

        diezDiasDespues.InstaladaAtUtc.Should().Be(Instalacion,
            "reabrir la app no puede reiniciar el demo");
    }

    [Fact]
    public async Task ObtenerAsync_NoDuplicaLaFilaConDosTerminalesALaVez()
    {
        // Dos cajas abriendo al mismo tiempo contra la misma base
        await Task.WhenAll(
            _repo.ObtenerAsync(Instalacion),
            _repo.ObtenerAsync(Instalacion),
            _repo.ObtenerAsync(Instalacion));

        (await ContarFilas()).Should().Be(1);
    }

    [Fact]
    public async Task RegistrarAperturaAsync_AvanzaLaUltimaApertura()
    {
        await _repo.ObtenerAsync(Instalacion);

        await _repo.RegistrarAperturaAsync(Instalacion.AddDays(3));

        (await _repo.ObtenerAsync(Instalacion)).UltimaAperturaUtc
            .Should().Be(Instalacion.AddDays(3));
    }

    [Fact]
    public async Task RegistrarAperturaAsync_NoBorraLaPruebaDelRelojAtrasado()
    {
        await _repo.ObtenerAsync(Instalacion);
        await _repo.RegistrarAperturaAsync(Instalacion.AddDays(10));

        // Movieron la fecha de Windows para atrás y abrieron la app
        await _repo.RegistrarAperturaAsync(Instalacion.AddDays(2));

        (await _repo.ObtenerAsync(Instalacion)).UltimaAperturaUtc
            .Should().Be(Instalacion.AddDays(10), "GREATEST no deja que la marca retroceda");
    }

    [Fact]
    public async Task RetrasarInicioAsync_MueveLaFechaHaciaAtras()
    {
        // Borraron la base: la fila se recrea con la fecha de hoy…
        await _repo.ObtenerAsync(Instalacion.AddDays(20));

        // …pero el ancla del disco dice que empezó hace 20 días
        await _repo.RetrasarInicioAsync(Instalacion);

        (await _repo.ObtenerAsync(Instalacion)).InstaladaAtUtc.Should().Be(Instalacion);
    }

    [Fact]
    public async Task RetrasarInicioAsync_NuncaRegalaDias()
    {
        await _repo.ObtenerAsync(Instalacion);

        await _repo.RetrasarInicioAsync(Instalacion.AddDays(10));

        (await _repo.ObtenerAsync(Instalacion)).InstaladaAtUtc.Should().Be(Instalacion);
    }

    [Fact]
    public async Task ActivarAsync_DejaConstanciaDeQuienYCuando()
    {
        await _repo.ObtenerAsync(Instalacion);

        await _repo.ActivarAsync("admin", Instalacion.AddDays(3));

        var licencia = await _repo.ObtenerAsync(Instalacion);
        licencia.Activada.Should().BeTrue();
        licencia.ActivadaAtUtc.Should().Be(Instalacion.AddDays(3));
        licencia.ActivadaPor.Should().Be("admin");
    }

    [Fact]
    public async Task ActivarAsync_AceptaQueNoHayaSesionIniciada()
    {
        // La ventana de activación aparece antes del login
        await _repo.ObtenerAsync(Instalacion);

        await _repo.ActivarAsync(null, Instalacion);

        var licencia = await _repo.ObtenerAsync(Instalacion);
        licencia.Activada.Should().BeTrue();
        licencia.ActivadaPor.Should().BeNull();
    }

    [Fact]
    public async Task ActivarAsync_EsIdempotente()
    {
        await _repo.ObtenerAsync(Instalacion);
        await _repo.ActivarAsync("admin", Instalacion.AddDays(3));

        await _repo.ActivarAsync("cajera", Instalacion.AddDays(9));

        var licencia = await _repo.ObtenerAsync(Instalacion);
        licencia.ActivadaPor.Should().Be("admin", "la segunda activación no pisa la primera");
        licencia.ActivadaAtUtc.Should().Be(Instalacion.AddDays(3));
    }

    [Fact]
    public async Task ActualizarEsquemaAsync_CreaLaTablaEnUnaInstalacionVieja()
    {
        // Una instalación 1.0.x: tiene la base pero no la tabla licencia
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP TABLE IF EXISTS licencia;");
        }

        var problema = await new VerificadorBaseDatos(CadenaTest).ActualizarEsquemaAsync();

        problema.Should().BeNull();
        (await _repo.ObtenerAsync(Instalacion)).InstaladaAtUtc.Should().Be(Instalacion);
    }

    [Fact]
    public async Task ActualizarEsquemaAsync_NoTocaLaTablaSiYaExiste()
    {
        await _repo.ObtenerAsync(Instalacion);
        await _repo.ActivarAsync("admin", Instalacion);

        var problema = await new VerificadorBaseDatos(CadenaTest).ActualizarEsquemaAsync();

        problema.Should().BeNull();
        (await _repo.ObtenerAsync(Instalacion)).Activada.Should().BeTrue(
            "arrancar la app no puede desactivar una licencia ya pagada");
    }

    // =========================================================
    // Los parches de apertura (migraciones 009 y 010)
    //
    // La app los corre sola en cada arranque para que actualizar no obligue a
    // nadie a abrir Workbench. Si acá falla algo, la clínica abre la app y le
    // revienta la ficha del paciente o el cobro, sin ninguna pista de por qué.
    // =========================================================

    [Theory]
    [InlineData("cliente", "ultima_visita_previa")]
    [InlineData("detalle", "precio_catalogo")]
    public async Task ActualizarEsquemaAsync_AgregaLasColumnasQueFaltan(string tabla, string columna)
    {
        // Una instalación anterior: la columna todavía no está
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, $"ALTER TABLE {tabla} DROP COLUMN {columna};");
        }

        var problema = await new VerificadorBaseDatos(CadenaTest).ActualizarEsquemaAsync();

        problema.Should().BeNull();
        (await ExisteColumna(tabla, columna)).Should().BeTrue();
    }

    /// <summary>
    /// Correrlo dos veces no puede fallar: se ejecuta en CADA arranque. Es el
    /// caso que rompió la primera versión de esto —un ALTER a ciegas daba
    /// "Duplicate column name" en la segunda apertura— y también el que
    /// destapó que las variables de usuario de MySQL (<c>SET @x :=</c>) no
    /// funcionan sin <c>Allow User Variables=true</c> en la cadena.
    /// </summary>
    [Fact]
    public async Task ActualizarEsquemaAsync_CorrerloDosVeces_NoFalla()
    {
        var verificador = new VerificadorBaseDatos(CadenaTest);

        (await verificador.ActualizarEsquemaAsync()).Should().BeNull();
        (await verificador.ActualizarEsquemaAsync()).Should().BeNull(
            "la app lo corre en cada arranque");
    }

    /// <summary>
    /// El permiso nuevo no alcanza con dárselo al rol: los usuarios YA creados
    /// tienen sus permisos copiados en usuario_permiso por el trigger del alta.
    /// Sin esta parte, el Admin de la clínica actualizaría y seguiría sin poder
    /// rebajar precios.
    /// </summary>
    [Fact]
    public async Task ActualizarEsquemaAsync_LeDaElPermisoNuevoAlAdminQueYaExistia()
    {
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            // Se simula la instalación vieja: sin el permiso en ninguna tabla
            await Ejecutar(conexion, """
                DELETE up FROM usuario_permiso up JOIN permiso p ON p.id = up.permiso_id
                 WHERE p.codigo = 'precio_editar';
                """);
            await Ejecutar(conexion, """
                DELETE rp FROM rol_permiso rp JOIN permiso p ON p.id = rp.permiso_id
                 WHERE p.codigo = 'precio_editar';
                """);
            await Ejecutar(conexion, "DELETE FROM permiso WHERE codigo = 'precio_editar';");
            await Ejecutar(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('admin_viejo', 'hash', 'Admin Viejo',
                        (SELECT id FROM rol WHERE nombre = 'Admin'));
                """);
        }

        var problema = await new VerificadorBaseDatos(CadenaTest).ActualizarEsquemaAsync();

        problema.Should().BeNull();
        (await TienePermisoEfectivo("admin_viejo", "precio_editar")).Should().BeTrue();
    }

    private async Task<bool> ExisteColumna(string tabla, string columna)
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.columns
             WHERE table_schema = DATABASE() AND table_name = @tabla AND column_name = @columna;
            """;
        cmd.Parameters.AddWithValue("@tabla", tabla);
        cmd.Parameters.AddWithValue("@columna", columna);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<bool> TienePermisoEfectivo(string username, string codigo)
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM usuario_permiso up
              JOIN usuario u ON u.id = up.usuario_id
              JOIN permiso p ON p.id = up.permiso_id
             WHERE u.username = @usuario AND p.codigo = @codigo;
            """;
        cmd.Parameters.AddWithValue("@usuario", username);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<long> ContarFilas()
    {
        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM licencia;";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
