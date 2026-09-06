using FluentAssertions;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Semáforo de las deudas de pacientes (012).
///
/// Se prueba entero porque de acá salen dos cosas que la clínica va a mirar
/// todos los días: a quién pinta de rojo la pantalla, y a quién nombra el
/// correo automático. Un umbral corrido un día hace que a alguien no se le
/// llame.
/// </summary>
public class CalculadoraFiadoTests
{
    private static readonly DateOnly Hoy = new(2026, 9, 6);

    // ------------------------------------------------------------------
    // El saldo manda
    // ------------------------------------------------------------------

    [Fact]
    public void SaldoCero_EsPagado_AunqueLaFechaHayaPasadoHaceMeses()
    {
        // Pagar tarde sigue siendo pagar: una deuda saldada no se reclama.
        CalculadoraFiado.Calcular(0m, new DateOnly(2026, 1, 1), Hoy)
            .Should().Be(SemaforoFiado.Pagado);
    }

    [Fact]
    public void SaldoNegativo_TambienEsPagado()
    {
        // No debería pasar, pero si pasa es "no debe nada", no un estado raro.
        CalculadoraFiado.Calcular(-50m, new DateOnly(2026, 1, 1), Hoy)
            .Should().Be(SemaforoFiado.Pagado);
    }

    // ------------------------------------------------------------------
    // Sin fecha acordada
    // ------------------------------------------------------------------

    [Fact]
    public void SinFechaAcordada_EstaAlDia_NoEnMora()
    {
        // La decisión de fondo: la clínica puede fiar sin fijar día. Pintar eso
        // de rojo acusaría de atrasado a quien nunca prometió una fecha.
        CalculadoraFiado.Calcular(1000m, null, Hoy).Should().Be(SemaforoFiado.AlDia);
    }

    [Fact]
    public void SinFechaAcordada_NoTieneDiasDeAtraso()
    {
        CalculadoraFiado.DiasDeAtraso(null, Hoy).Should().Be(0);
        CalculadoraFiado.DiasRestantes(null, Hoy).Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Los umbrales, uno por uno
    // ------------------------------------------------------------------

    [Theory]
    // Falta más de una semana
    [InlineData(30, SemaforoFiado.AlDia)]
    [InlineData(8, SemaforoFiado.AlDia)]
    // Justo en el borde de los 7 días: todavía al día
    [InlineData(7, SemaforoFiado.PorVencer)]
    [InlineData(1, SemaforoFiado.PorVencer)]
    // Vence HOY: aún no está atrasado, el día no terminó
    [InlineData(0, SemaforoFiado.PorVencer)]
    // Atrasado, pero dentro de los 15 días
    [InlineData(-1, SemaforoFiado.Vencido)]
    [InlineData(-15, SemaforoFiado.Vencido)]
    // Pasados los 15 días hay que llamar
    [InlineData(-16, SemaforoFiado.EnMora)]
    [InlineData(-200, SemaforoFiado.EnMora)]
    public void Umbrales(int diasDesdeHoy, SemaforoFiado esperado)
    {
        var fecha = Hoy.AddDays(diasDesdeHoy);
        CalculadoraFiado.Calcular(500m, fecha, Hoy).Should().Be(esperado);
    }

    [Fact]
    public void ElDiaDelCompromiso_NoCuentaComoAtraso()
    {
        // Le dijeron "el 6"; el 6 a las 9 de la mañana no está atrasado.
        CalculadoraFiado.DiasDeAtraso(Hoy, Hoy).Should().Be(0);
        CalculadoraFiado.Calcular(500m, Hoy, Hoy).Should().Be(SemaforoFiado.PorVencer);
    }

    [Fact]
    public void DiasDeAtraso_NuncaEsNegativo()
    {
        CalculadoraFiado.DiasDeAtraso(Hoy.AddDays(10), Hoy).Should().Be(0);
        CalculadoraFiado.DiasDeAtraso(Hoy.AddDays(-3), Hoy).Should().Be(3);
    }

    // ------------------------------------------------------------------
    // Cómo se lee en pantalla y en el correo
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0, "vence HOY")]
    [InlineData(1, "vence mañana")]
    [InlineData(5, "vence en 5 días")]
    [InlineData(-1, "venció ayer")]
    [InlineData(-9, "atrasado 9 días")]
    public void DescribirVencimiento(int diasDesdeHoy, string esperado)
    {
        CalculadoraFiado.DescribirVencimiento(Hoy.AddDays(diasDesdeHoy), Hoy)
            .Should().Be(esperado);
    }

    [Fact]
    public void DescribirVencimiento_SinFecha_LoDiceEnLugarDeInventarUna()
    {
        CalculadoraFiado.DescribirVencimiento(null, Hoy).Should().Be("sin fecha acordada");
    }
}
