using FluentAssertions;
using MED100.Common;

namespace MED100.Services.Tests;

/// <summary>
/// El control de acceso de toda la app depende de SesionActual:
/// estos tests fijan su contrato (permisos, rol, limpieza al cerrar).
/// </summary>
public class SesionActualTests : IDisposable
{
    public void Dispose() => SesionActual.Cerrar();

    [Fact]
    public void Iniciar_CargaRolYPermisos()
    {
        SesionActual.Iniciar(1, "maria", "María Gómez", "Cajero",
            ["vender", "clientes"], DateTime.UtcNow, 10);

        SesionActual.HaySesionActiva.Should().BeTrue();
        SesionActual.EsAdmin.Should().BeFalse();
        SesionActual.TienePermiso("vender").Should().BeTrue();
        SesionActual.TienePermiso("configuracion").Should().BeFalse();
    }

    [Fact]
    public void Cerrar_LimpiaTodoIncluyendoPermisos()
    {
        SesionActual.Iniciar(1, "admin", "Admin", "Admin",
            ["configuracion", "usuarios"], DateTime.UtcNow, 10);

        SesionActual.Cerrar();

        SesionActual.HaySesionActiva.Should().BeFalse();
        SesionActual.EsAdmin.Should().BeFalse();
        SesionActual.TienePermiso("configuracion").Should().BeFalse();
    }

    [Fact]
    public void OtroLogin_NoHeredaPermisosDelAnterior()
    {
        // Regla anti-patrón: nada del usuario anterior puede sobrevivir
        SesionActual.Iniciar(1, "admin", "Admin", "Admin",
            ["configuracion", "usuarios", "vender"], DateTime.UtcNow, 10);
        SesionActual.Cerrar();
        SesionActual.Iniciar(2, "pedro", "Pedro", "Servicio",
            ["vender", "clientes", "clientes_editar"], DateTime.UtcNow, 11);

        SesionActual.TienePermiso("configuracion").Should().BeFalse();
        SesionActual.TienePermiso("clientes_editar").Should().BeTrue();
        SesionActual.Rol.Should().Be("Servicio");
    }
}
