using FluentAssertions;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>Cálculos puros de la analítica: rangos de fecha y variación mensual.</summary>
public class AnaliticaServiceTests
{
    // Domingo 12/07/2026 (para probar el borde de la semana)
    private static readonly DateOnly Domingo = new(2026, 7, 12);
    private static readonly DateOnly Miercoles = new(2026, 7, 15);

    [Fact]
    public void CalcularRango_Hoy()
    {
        var (desde, hasta) = AnaliticaService.CalcularRango(RangoReporte.Hoy, Miercoles);
        desde.Should().Be(Miercoles);
        hasta.Should().Be(Miercoles);
    }

    [Fact]
    public void CalcularRango_Ayer()
    {
        var (desde, hasta) = AnaliticaService.CalcularRango(RangoReporte.Ayer, Miercoles);
        desde.Should().Be(new DateOnly(2026, 7, 14));
        hasta.Should().Be(new DateOnly(2026, 7, 14));
    }

    [Fact]
    public void CalcularRango_EstaSemana_EmpiezaElLunes()
    {
        var (desde, hasta) = AnaliticaService.CalcularRango(RangoReporte.EstaSemana, Miercoles);
        desde.Should().Be(new DateOnly(2026, 7, 13));   // lunes
        hasta.Should().Be(Miercoles);
    }

    [Fact]
    public void CalcularRango_EstaSemana_EnDomingo_ElLunesEsElDeHace6Dias()
    {
        // El domingo cierra la semana, no la abre (convención comercial RD)
        var (desde, hasta) = AnaliticaService.CalcularRango(RangoReporte.EstaSemana, Domingo);
        desde.Should().Be(new DateOnly(2026, 7, 6));    // lunes anterior
        hasta.Should().Be(Domingo);
    }

    [Fact]
    public void CalcularRango_EsteMes_DesdeElPrimeroHastaHoy()
    {
        var (desde, hasta) = AnaliticaService.CalcularRango(RangoReporte.EsteMes, Miercoles);
        desde.Should().Be(new DateOnly(2026, 7, 1));
        hasta.Should().Be(Miercoles);
    }

    [Fact]
    public void CalcularRango_MesPasado_MesCompleto()
    {
        var (desde, hasta) = AnaliticaService.CalcularRango(RangoReporte.MesPasado, Miercoles);
        desde.Should().Be(new DateOnly(2026, 6, 1));
        hasta.Should().Be(new DateOnly(2026, 6, 30));   // junio tiene 30 días
    }

    [Fact]
    public void CalcularRango_MesPasado_EnEnero_CruzaDeAnio()
    {
        var (desde, hasta) = AnaliticaService.CalcularRango(
            RangoReporte.MesPasado, new DateOnly(2026, 1, 10));

        desde.Should().Be(new DateOnly(2025, 12, 1));
        hasta.Should().Be(new DateOnly(2025, 12, 31));
    }

    [Theory]
    [InlineData(150, 100, "+50% vs. mes anterior", true)]
    [InlineData(80, 100, "-20% vs. mes anterior", false)]
    [InlineData(100, 100, "+0% vs. mes anterior", true)]
    public void CalcularVariacion_ComparaContraElMesAnterior(
        decimal actual, decimal anterior, string textoEsperado, bool positivoEsperado)
    {
        var (texto, positivo) = AnaliticaService.CalcularVariacion(actual, anterior);

        texto.Should().Be(textoEsperado);
        positivo.Should().Be(positivoEsperado);
    }

    [Fact]
    public void CalcularVariacion_SinMesAnterior_NoDivideEntreCero()
    {
        AnaliticaService.CalcularVariacion(500m, 0m).Texto.Should().Be("Sin ventas el mes anterior");
        AnaliticaService.CalcularVariacion(0m, 0m).Texto.Should().Be("Sin datos del mes anterior");
    }
}
