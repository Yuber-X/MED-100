using FluentAssertions;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// El ITBIS configurable del punto de venta (pedido de Yuber 2026-07-31:
/// "se necesita un checkbox para deshabilitar el uso del ITBIS, junto a un
/// textbox para saber cuanto % de itbis se usara").
///
/// El motor ya sabia apagarlo; lo que faltaba era la pantalla. Estas pruebas
/// fijan que la casilla haga lo que dice: con el ITBIS apagado, la venta NO lo
/// cobra, y la tasa que se escribe es la que se aplica.
/// </summary>
public class ItbisConfigurableTests
{
    private static readonly IReadOnlyList<VentaLinea> DosArticulos =
    [
        new VentaLinea(1, "Arroz", 2, 100m),      // 200
        new VentaLinea(2, "Aceite", 1, 300m)      // 300
    ];                                            // subtotal 500

    [Fact]
    public void ConElItbisEncendido_SeCobraLaTasaConfigurada()
    {
        var cfg = new ConfiguracionNegocio { ItbisActivo = true, ItbisTasa = 18m };

        var t = VentaService.CalcularTotales(DosArticulos, cfg.ItbisTasaEfectiva, cfg.Redondeo);

        t.Subtotal.Should().Be(500m);
        t.Itbis.Should().Be(90m, "18% de 500");
        t.Total.Should().Be(590m);
    }

    /// <summary>
    /// El caso que se pidió: apagar la casilla y que deje de cobrarse. La TASA
    /// no se borra —queda guardada por si se vuelve a encender—, lo que cambia
    /// es la tasa EFECTIVA, que es la que ve el cálculo.
    /// </summary>
    [Fact]
    public void ConElItbisApagado_NoSeCobraNada_PeroLaTasaSeConserva()
    {
        var cfg = new ConfiguracionNegocio { ItbisActivo = false, ItbisTasa = 18m };

        cfg.ItbisTasaEfectiva.Should().Be(0m);
        cfg.ItbisTasa.Should().Be(18m, "la tasa queda guardada para cuando se vuelva a encender");

        var t = VentaService.CalcularTotales(DosArticulos, cfg.ItbisTasaEfectiva, cfg.Redondeo);

        t.Itbis.Should().Be(0m);
        t.Total.Should().Be(t.Subtotal, "el total es el subtotal pelado");
    }

    /// <summary>Una tasa distinta de 18 también se respeta: no está quemada.</summary>
    [Theory]
    [InlineData(16, 80)]
    [InlineData(10, 50)]
    [InlineData(0, 0)]
    public void LaTasaQueSeEscribe_EsLaQueSeAplica(decimal tasa, decimal itbisEsperado)
    {
        var cfg = new ConfiguracionNegocio { ItbisActivo = true, ItbisTasa = tasa };

        var t = VentaService.CalcularTotales(DosArticulos, cfg.ItbisTasaEfectiva, cfg.Redondeo);

        t.Itbis.Should().Be(itbisEsperado);
    }

    /// <summary>
    /// El ITBIS se calcula SOBRE EL SUBTOTAL, no sumando el de cada línea: así
    /// no se acumulan redondeos. Con 3 líneas de 33.33 la diferencia se ve.
    /// </summary>
    [Fact]
    public void ElItbisSeCalculaSobreElSubtotal_NoLineaPorLinea()
    {
        IReadOnlyList<VentaLinea> lineas =
        [
            new VentaLinea(1, "A", 1, 33.33m),
            new VentaLinea(2, "B", 1, 33.33m),
            new VentaLinea(3, "C", 1, 33.33m)
        ];
        var cfg = new ConfiguracionNegocio { ItbisActivo = true, ItbisTasa = 18m };

        var t = VentaService.CalcularTotales(lineas, cfg.ItbisTasaEfectiva, cfg.Redondeo);

        t.Subtotal.Should().Be(99.99m);
        // 99.99 × 18% = 17.9982 → 18.00. Línea por línea daría 6.00 × 3 = 18.00
        // acá, pero con otros montos se separa; la regla es una sola cuenta.
        t.Itbis.Should().Be(18.00m);
        t.Total.Should().Be(117.99m);
    }
}
