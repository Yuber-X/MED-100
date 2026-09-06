using FluentAssertions;
using MED100.Models;

namespace MED100.Services.Tests;

/// <summary>
/// La secuencia de comprobantes (011). Es lógica pura: formatear un número,
/// saber si el rango se acabó o venció, y descomponer un NCF escrito a mano.
///
/// Se prueba a fondo porque un error acá no se ve en pantalla: se ve semanas
/// después, en el libro de ventas que la clínica le lleva a la DGII.
/// </summary>
public class NcfSecuenciaTests
{
    private static NcfSecuencia Base() => new()
    {
        Prefijo = "B02",
        Largo = 8,
        Proxima = 1,
        Activo = true
    };

    // ------------------------------------------------------------------
    // Formato
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, "B0200000001")]
    [InlineData(12, "B0200000012")]
    [InlineData(99999999, "B0299999999")]
    public void Formatear_RellenaConCerosHastaElLargo(long numero, string esperado)
    {
        Base().Formatear(numero).Should().Be(esperado);
    }

    [Fact]
    public void Formatear_ConLargo10_EsUnECf()
    {
        var s = Base();
        s.Prefijo = "E32";
        s.Largo = 10;
        s.Formatear(45).Should().Be("E320000000045");
    }

    [Fact]
    public void Formatear_NoRecortaUnNumeroMasLargoQueElLargo()
    {
        // PadLeft no trunca. Si esto llegara a pasar significa que el rango
        // autorizado se cargó mal; es preferible un NCF largo y visible a uno
        // recortado que parezca válido.
        Base().Formatear(123456789).Should().Be("B02123456789");
    }

    // ------------------------------------------------------------------
    // Fin de rango
    // ------------------------------------------------------------------

    [Fact]
    public void SinFinDeRango_NuncaSeAgotaYNoSabeCuantosQuedan()
    {
        var s = Base();
        s.FinRango = null;
        s.EstaAgotada.Should().BeFalse();
        s.Restantes.Should().BeNull();
    }

    [Fact]
    public void Restantes_CuentaElNumeroEnCursoComoDisponible()
    {
        var s = Base();
        s.Proxima = 10;
        s.FinRango = 10;
        // El 10 todavía no se entregó: queda uno, no cero.
        s.Restantes.Should().Be(1);
        s.EstaAgotada.Should().BeFalse();
    }

    [Fact]
    public void SeAgota_CuandoLaProximaPasaElFinDelRango()
    {
        var s = Base();
        s.Proxima = 11;
        s.FinRango = 10;
        s.EstaAgotada.Should().BeTrue();
        s.Restantes.Should().Be(0);
    }

    [Fact]
    public void Restantes_NuncaEsNegativo()
    {
        var s = Base();
        s.Proxima = 50;
        s.FinRango = 10;
        s.Restantes.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Vencimiento
    // ------------------------------------------------------------------

    [Fact]
    public void SinVencimiento_NuncaVence()
    {
        Base().EstaVencida(new DateOnly(2030, 1, 1)).Should().BeFalse();
    }

    [Fact]
    public void ElDiaDelVencimiento_TODAVIA_SirveDentro()
    {
        var s = Base();
        s.Vencimiento = new DateOnly(2026, 12, 31);
        // La autorización vale HASTA esa fecha inclusive: cortarla un día antes
        // le quitaría a la clínica un día entero de comprobantes válidos.
        s.EstaVencida(new DateOnly(2026, 12, 31)).Should().BeFalse();
        s.EstaVencida(new DateOnly(2027, 1, 1)).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Descomponer: adoptar un NCF escrito a mano
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("B0200000045", "B02", 45L, 8)]
    [InlineData("E320000000045", "E32", 45L, 10)]
    [InlineData("b0200000045", "B02", 45L, 8)]      // se normaliza a mayúsculas
    [InlineData("  B0200000045  ", "B02", 45L, 8)]  // y se recortan los espacios
    public void Descomponer_ParteUnNcfBienFormado(
        string texto, string prefijo, long numero, int largo)
    {
        var partes = NcfSecuencia.Descomponer(texto);

        partes.Should().NotBeNull();
        partes!.Value.Prefijo.Should().Be(prefijo);
        partes.Value.Numero.Should().Be(numero);
        partes.Value.Largo.Should().Be(largo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hola")]
    [InlineData("B02")]              // sin número
    [InlineData("B0212345")]         // 5 dígitos: menos del mínimo
    [InlineData("B021234567890123")] // más del máximo
    [InlineData("BB200000045")]      // dos letras de serie
    [InlineData("A010000000000000045")] // formato viejo pre-2018, fuera a propósito
    public void Descomponer_DevuelveNullSiNoTieneFormaDeComprobante(string? texto)
    {
        // Adivinar la numeración del libro de ventas es peor que no hacer nada:
        // ante cualquier duda no se toca la secuencia.
        NcfSecuencia.Descomponer(texto).Should().BeNull();
    }
}
