using FluentAssertions;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Lo que se entiende de una cédula escaneada (pedido de la clínica 2026-09-21).
///
/// El formato del código de la cédula dominicana no está documentado y cambia
/// entre emisiones, así que lo que se prueba acá no es "parsea el formato X":
/// es que reconozca sin dudas lo reconocible y que NO invente el resto. Un
/// nombre inventado a partir de basura termina impreso en una factura.
/// </summary>
public class LectorCedulaTests
{
    [Theory]
    [InlineData("40212345678")]
    [InlineData("402-1234567-8")]
    [InlineData("402 1234567 8")]
    [InlineData("  402-1234567-8  ")]
    public void Cedula_LaDevuelveSiempreConElFormatoDeRd(string crudo)
    {
        LectorCedula.Interpretar(crudo).Cedula.Should().Be("402-1234567-8");
    }

    [Fact]
    public void Cedula_LaEncuentraAunqueVengaEntreOtrosDatos()
    {
        var leido = LectorCedula.Interpretar("ID|40212345678|RD|2031-05-12");

        leido.Cedula.Should().Be("402-1234567-8");
    }

    [Theory]
    [InlineData("4021234567")]      // 10 dígitos
    [InlineData("402123456789")]    // 12 dígitos
    [InlineData("sin numeros")]
    [InlineData("")]
    public void Cedula_SiNoHayOnceDigitos_NoInventaNada(string crudo)
    {
        LectorCedula.Interpretar(crudo).Cedula.Should().BeNull();
    }

    [Fact]
    public void Nombre_SoloCuandoElContenidoVieneEnCampos()
    {
        var leido = LectorCedula.Interpretar("40212345678|JUAN PEREZ MARTINEZ|M|15/03/1990");

        leido.Nombre.Should().Be("Juan Perez Martinez");
        leido.Sexo.Should().Be(SexoPaciente.Masculino);
        leido.FechaNacimiento.Should().Be(new DateOnly(1990, 3, 15));
    }

    [Fact]
    public void Nombre_EnUnTextoCorrido_NoSeAdivina()
    {
        // Sin separadores no hay forma de saber qué es nombre y qué es ruido.
        var leido = LectorCedula.Interpretar("40212345678 RD DOM 2031");

        leido.Nombre.Should().BeNull();
        leido.Cedula.Should().Be("402-1234567-8", "la cédula sí se reconoce sola");
    }

    [Fact]
    public void Nombre_UneLosDosCamposCuandoVienenSeparados()
    {
        var leido = LectorCedula.Interpretar("40212345678^MARIA ALTAGRACIA^PEREZ DE LOS SANTOS^F");

        leido.Nombre.Should().Be("Maria Altagracia Perez de los Santos");
        leido.Sexo.Should().Be(SexoPaciente.Femenino);
    }

    [Fact]
    public void Fecha_LaCompactaTambienSeEntiende()
    {
        LectorCedula.Interpretar("40212345678|19900315").FechaNacimiento
            .Should().Be(new DateOnly(1990, 3, 15));
    }

    [Fact]
    public void Fecha_UnNumeroCualquieraNoPasaPorFechaDeNacimiento()
    {
        // 20351231 es una fecha válida pero está en el futuro: es la fecha de
        // vencimiento de la cédula, no el nacimiento de nadie.
        LectorCedula.Interpretar("40212345678|20351231").FechaNacimiento.Should().BeNull();
    }

    [Fact]
    public void Sexo_UnaEmeSueltaDentroDeUnaPalabraNoCuenta()
    {
        LectorCedula.Interpretar("40212345678|DOMINICANA").Sexo.Should().BeNull();
    }

    [Fact]
    public void NoSeEntendioNada_LoDice()
    {
        var leido = LectorCedula.Interpretar("QRCODE-BASURA");

        leido.HayAlgo.Should().BeFalse();
        leido.Crudo.Should().Be("QRCODE-BASURA", "lo leído se muestra igual para revisarlo");
    }

    [Fact]
    public void Formatear_DevuelveNullSiNoSonOnceDigitos()
    {
        LectorCedula.Formatear("123").Should().BeNull();
        LectorCedula.Formatear("402-1234567-8").Should().Be("402-1234567-8");
    }
}
