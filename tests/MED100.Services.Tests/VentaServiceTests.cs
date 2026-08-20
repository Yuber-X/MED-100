using FluentAssertions;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Cálculos puros de venta (docs/ITBIS.md). Casos obligatorios de la spec §11.
/// </summary>
public class VentaServiceTests
{
    private static VentaLinea Linea(long id, int cantidad, decimal precio) =>
        new(id, $"Producto {id}", cantidad, precio);

    [Fact]
    public void CalcularTotales_VentaSimple_UnProducto()
    {
        var totales = VentaService.CalcularTotales(
            [Linea(1, 1, 100.00m)], 18.00m, ModoRedondeo.Centavo);

        totales.Subtotal.Should().Be(100.00m);
        totales.Itbis.Should().Be(18.00m);
        totales.Total.Should().Be(118.00m);
    }

    [Fact]
    public void CalcularTotales_MultiplesProductos_ItbisSobreSubtotal()
    {
        // 3 × 10.05 + 2 × 33.33 = 96.81 → ITBIS = R(17.4258) = 17.43
        // (por línea daría R(30.15×.18)=5.43 + R(66.66×.18)=12.00 = 17.43 aquí,
        //  pero el contrato es UN solo redondeo sobre el subtotal)
        var totales = VentaService.CalcularTotales(
            [Linea(1, 3, 10.05m), Linea(2, 2, 33.33m)], 18.00m, ModoRedondeo.Centavo);

        totales.Subtotal.Should().Be(96.81m);
        totales.Itbis.Should().Be(17.43m);
        totales.Total.Should().Be(114.24m);
    }

    [Fact]
    public void CalcularTotales_TasaCero_ProductosExentos()
    {
        var totales = VentaService.CalcularTotales(
            [Linea(1, 5, 20.00m)], 0m, ModoRedondeo.Centavo);

        totales.Itbis.Should().Be(0m);
        totales.Total.Should().Be(100.00m);
    }

    [Theory]
    [InlineData("centavo", 117.43)]
    [InlineData("peso", 117.00)]
    [InlineData("arriba", 118.00)]
    public void CalcularTotales_ModoRedondeo_SoloAfectaElTotal(string modo, decimal totalEsperado)
    {
        var redondeo = modo switch
        {
            "peso" => ModoRedondeo.Peso,
            "arriba" => ModoRedondeo.Arriba,
            _ => ModoRedondeo.Centavo
        };

        // subtotal 99.52 → itbis R(17.9136)=17.91 → bruto 117.43
        var totales = VentaService.CalcularTotales(
            [Linea(1, 1, 99.52m)], 18.00m, redondeo);

        totales.Subtotal.Should().Be(99.52m);
        totales.Itbis.Should().Be(17.91m);   // el ITBIS reportable no cambia
        totales.Total.Should().Be(totalEsperado);
    }

    [Fact]
    public void CalcularTotales_MitadDeCentavo_RedondeaLejosDeCero()
    {
        // subtotal 12.25 × 18% = 2.205 → AwayFromZero = 2.21 (banquero daría 2.20)
        var totales = VentaService.CalcularTotales(
            [Linea(1, 1, 12.25m)], 18.00m, ModoRedondeo.Centavo);

        totales.Itbis.Should().Be(2.21m);
    }

    [Fact]
    public void ItbisTasaEfectiva_CeroCuandoElItbisEstaDesactivado()
    {
        // Regla Yuber 2026-07-12: el ITBIS se puede apagar por completo
        var cfg = new ConfiguracionNegocio { ItbisActivo = false, ItbisTasa = 18.00m };

        cfg.ItbisTasaEfectiva.Should().Be(0m);

        var totales = VentaService.CalcularTotales(
            [Linea(1, 2, 50.00m)], cfg.ItbisTasaEfectiva, cfg.Redondeo);

        totales.Subtotal.Should().Be(100.00m);
        totales.Itbis.Should().Be(0m);
        totales.ItbisTasa.Should().Be(0m);   // la factura persiste tasa 0
        totales.Total.Should().Be(100.00m);  // total = subtotal, sin impuesto
    }

    [Fact]
    public void ItbisTasaEfectiva_UsaLaTasaCuandoEstaActivo()
    {
        var cfg = new ConfiguracionNegocio { ItbisActivo = true, ItbisTasa = 16.00m };

        cfg.ItbisTasaEfectiva.Should().Be(16.00m);

        VentaService.CalcularTotales([Linea(1, 1, 100.00m)], cfg.ItbisTasaEfectiva, cfg.Redondeo)
            .Total.Should().Be(116.00m);
    }

    [Fact]
    public void CalcularCambio_EfectivoExacto_CambioCero() =>
        VentaService.CalcularCambio(118.00m, 118.00m).Should().Be(0m);

    [Fact]
    public void CalcularCambio_ConVuelto() =>
        VentaService.CalcularCambio(200.00m, 114.24m).Should().Be(85.76m);

    [Theory]
    [InlineData(FormatoFactura.Simple, 1, "F-0001")]
    [InlineData(FormatoFactura.Simple, 12345, "F-12345")]
    [InlineData(FormatoFactura.ConAnio, 7, "F-2026-0007")]
    public void FormatearNumeroFactura_AmbosFormatos(FormatoFactura formato, long numero, string esperado) =>
        VentaService.FormatearNumeroFactura("F-", numero, formato, 2026).Should().Be(esperado);
}
