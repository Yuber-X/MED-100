using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Admin de Usuarios + permisos (regla Yuber 2026-07-12): solo el Admin crea
/// empleados, cambia contraseñas y ajusta permisos. Los permisos por defecto
/// los da el rol (triggers del POS-400); las casillas permiten afinarlos.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class UsuariosYPermisosTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_usuarios_test;";

    private UsuarioService _servicio = null!;
    private UsuarioRepository _repo = null!;
    private AuthService _auth = null!;
    private long _adminId;
    private int _rolCajeroId, _rolSupervisorId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_usuarios_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        var factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(factory));
        _repo = new UsuarioRepository(factory);
        _servicio = new UsuarioService(_repo, auditoria);
        _auth = new AuthService(_repo, new SesionRepository(factory), auditoria);

        // El wizard del primer arranque crea al Admin
        _adminId = await _auth.CrearCuentaInicialAsync("admin", "Ana Admin", "clave-admin-123");
        await _auth.LoginAsync("admin", "clave-admin-123");   // deja SesionActual como Admin

        var roles = await _repo.ObtenerRolesAsync();
        _rolCajeroId = roles.First(r => r.Nombre == "Cajero").Id;
        _rolSupervisorId = roles.First(r => r.Nombre == "Supervisor").Id;
    }

    public Task DisposeAsync()
    {
        SesionActual.Cerrar();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CrearUsuario_HeredaLosPermisosPorDefectoDeSuRol()
    {
        var id = await _servicio.CrearAsync("carlos", "Carlos", "Caja", _rolCajeroId, "clave-cajero-1");

        var permisos = await _servicio.ObtenerPermisosAsync(id);

        // Los del seed del rol Cajero (trigger trg_usuario_after_insert).
        // El Cajero cobra y atiende el mostrador: agenda, da turnos y registra
        // pacientes, pero no toca el tarifario ni el honorario de los médicos.
        //
        // 'clientes_editar' entró el 2026-08-14, al fusionar el viejo rol
        // Vendedor en Cajero: quien cobra también registra al paciente que
        // llega sin ficha, y mandarlo a buscar a otro para eso era absurdo.
        permisos.Should().BeEquivalentTo(
            ["vender", "clientes", "clientes_editar", "comprobantes", "cuadre", "citas", "turnos"]);
        permisos.Should().NotContain("configuracion");
        permisos.Should().NotContain("usuarios");
        permisos.Should().NotContain("medicos", "el tarifario y los honorarios no son del mostrador");
    }

    [Fact]
    public async Task CrearUsuario_LaContrasenaSeGuardaHasheada_YPermiteEntrar()
    {
        await _servicio.CrearAsync("pedro", "Pedro", null, _rolCajeroId, "clave-pedro-99");

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        var hash = await EscalarTextoAsync(conexion,
            "SELECT password_hash FROM usuario WHERE username='pedro';");
        hash.Should().NotBe("clave-pedro-99");        // jamás en texto plano
        hash.Should().StartWith("$2");                // BCrypt

        // Y el empleado puede iniciar sesión con ella
        (await _auth.LoginAsync("pedro", "clave-pedro-99")).Should().Be(ResultadoLogin.Exitoso);
        SesionActual.Rol.Should().Be("Cajero");
    }

    [Fact]
    public async Task AjustarPermisos_ElAdminPuedeQuitarEditarProductosYClientes()
    {
        var id = await _servicio.CrearAsync("sofia", "Sofía", null, _rolSupervisorId, "clave-sofia-1");
        var delRol = await _servicio.ObtenerPermisosAsync(id);
        delRol.Should().Contain("productos").And.Contain("clientes_editar");

        // El Admin le quita la capacidad de tocar productos y editar clientes
        var recortados = delRol.Except(["productos", "clientes_editar"]).ToList();
        await _servicio.GuardarPermisosAsync(id, recortados);

        var finales = await _servicio.ObtenerPermisosAsync(id);
        finales.Should().NotContain("productos");
        finales.Should().NotContain("clientes_editar");
        finales.Should().Contain("clientes");   // sigue pudiendo consultarlos
        finales.Should().Contain("vender");
    }

    [Fact]
    public async Task AjustarPermisos_ElAdminPuedeDarUnPermisoQueElRolNoTrae()
    {
        var id = await _servicio.CrearAsync("luis", "Luis", null, _rolCajeroId, "clave-luis-11");
        var permisos = await _servicio.ObtenerPermisosAsync(id);

        // Un Cajero normalmente no puede anular; este sí (override)
        await _servicio.GuardarPermisosAsync(id, [.. permisos, "facturas_anular"]);

        (await _servicio.ObtenerPermisosAsync(id)).Should().Contain("facturas_anular");
    }

    [Fact]
    public async Task CambiarRol_ResincronizaLosPermisosConLosDelRolNuevo()
    {
        var id = await _servicio.CrearAsync("marta", "Marta", null, _rolCajeroId, "clave-marta-1");
        var comoCajero = await _servicio.ObtenerPermisosAsync(id);
        comoCajero.Should().NotContain("reportes", "el Cajero no ve reportes");

        await _servicio.ActualizarAsync(id, "marta", "Marta", null, _rolSupervisorId, activo: true);

        // El trigger trg_usuario_after_update copió los permisos del Supervisor.
        // Se comprueba QUÉ ganó y qué sigue sin tener, no cuántos son: un
        // conteo fijo obliga a tocar el test cada vez que se agrega un permiso.
        var permisos = await _servicio.ObtenerPermisosAsync(id);
        permisos.Should().Contain("reportes");
        permisos.Should().Contain("medicos", "el Supervisor sí administra los médicos");
        permisos.Should().NotContain("configuracion", "eso es exclusivo del Admin");
        permisos.Should().NotContain("usuarios");
        permisos.Count.Should().BeGreaterThan(comoCajero.Count);
    }

    [Fact]
    public async Task CambiarPassword_ElAdminLaRestableceSinSaberLaAnterior()
    {
        var id = await _servicio.CrearAsync("olvidadizo", "Olvidadizo", null, _rolCajeroId, "clave-vieja-1");

        await _servicio.CambiarPasswordAsync(id, "clave-nueva-2");

        (await _auth.LoginAsync("olvidadizo", "clave-vieja-1"))
            .Should().Be(ResultadoLogin.CredencialesInvalidas);
        (await _auth.LoginAsync("olvidadizo", "clave-nueva-2"))
            .Should().Be(ResultadoLogin.Exitoso);
    }

    [Fact]
    public async Task SinSerAdmin_NoSePuedeCrearUsuarioNiCambiarPasswords()
    {
        var id = await _servicio.CrearAsync("cajero", "Cajero", null, _rolCajeroId, "clave-cajero-9");

        // Entra el Cajero: pierde el permiso 'usuarios'
        await _auth.LoginAsync("cajero", "clave-cajero-9");

        var crear = () => _servicio.CrearAsync("intruso", "Intruso", null, _rolCajeroId, "clave-intr-1");
        await crear.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Solo el Administrador*");

        var cambiar = () => _servicio.CambiarPasswordAsync(_adminId, "hackeada-123");
        await cambiar.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Solo el Administrador*");

        var listar = () => _servicio.ObtenerTodosAsync();
        await listar.Should().ThrowAsync<InvalidOperationException>();

        // Y la contraseña del Admin sigue funcionando
        (await _auth.LoginAsync("admin", "clave-admin-123")).Should().Be(ResultadoLogin.Exitoso);
        _ = id;
    }

    [Fact]
    public async Task ElAdmin_NoPuedeQuitarseSusPropiosPermisosDeAdministracion()
    {
        var accion = () => _servicio.GuardarPermisosAsync(_adminId, ["vender", "clientes"]);

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*No puedes quitarte a ti mismo*");
    }

    [Fact]
    public async Task ElAdmin_NoPuedeDesactivarseASiMismo()
    {
        var rolAdminId = (await _repo.ObtenerRolesAsync()).First(r => r.Nombre == "Admin").Id;

        var accion = () => _servicio.ActualizarAsync(_adminId, "admin", "Ana", "Admin",
            rolAdminId, activo: false);

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*No puedes desactivar tu propia cuenta*");
    }

    [Fact]
    public async Task CrearUsuario_ConPasswordCorta_Falla()
    {
        var accion = () => _servicio.CrearAsync("corto", "Corto", null, _rolCajeroId, "1234");

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*al menos 8*");
    }

    [Fact]
    public async Task CrearUsuario_ConUsernameRepetido_Falla()
    {
        await _servicio.CrearAsync("repetido", "Uno", null, _rolCajeroId, "clave-uno-123");

        var accion = () => _servicio.CrearAsync("repetido", "Dos", null, _rolCajeroId, "clave-dos-123");

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*Ya existe un usuario*");
    }

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> EscalarTextoAsync(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return (await cmd.ExecuteScalarAsync())?.ToString() ?? string.Empty;
    }
}
