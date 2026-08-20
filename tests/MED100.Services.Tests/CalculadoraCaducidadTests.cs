using FluentAssertions;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Semáforo de caducidad: 100% de ramas (spec §11). Regla mensual heredada
/// del POS-400: Verde ≥7m · Amarillo 4–6m · Naranja 2–3m · Rojo ≤1m.
/// </summary>
public class CalculadoraCaducidadTests
{
    private static readonly DateOnly Hoy = new(2026, 7, 11);

    [Theory]
    // Rojo: caducado o 1 mes o menos
    [InlineData(2026, 6, 1, SemaforoCaducidad.Rojo)]     // ya caducado
    [InlineData(2026, 7, 11, SemaforoCaducidad.Rojo)]    // caduca hoy
    [InlineData(2026, 8, 10, SemaforoCaducidad.Rojo)]    // 0 meses completos
    [InlineData(2026, 8, 11, SemaforoCaducidad.Rojo)]    // exactamente 1 mes
    // Naranja: 2–3 meses
    [InlineData(2026, 9, 11, SemaforoCaducidad.Naranja)]  // 2 meses
    [InlineData(2026, 10, 11, SemaforoCaducidad.Naranja)] // 3 meses
    [InlineData(2026, 11, 10, SemaforoCaducidad.Naranja)] // 3 meses (día anterior)
    // Amarillo: 4–6 meses
    [InlineData(2026, 11, 11, SemaforoCaducidad.Amarillo)] // 4 meses
    [InlineData(2027, 1, 11, SemaforoCaducidad.Amarillo)]  // 6 meses
    // Verde: 7+ meses
    [InlineData(2027, 2, 11, SemaforoCaducidad.Verde)]     // 7 meses
    [InlineData(2028, 7, 11, SemaforoCaducidad.Verde)]     // 2 años
    public void Calcular_MapeaMesesRestantesAlColorCorrecto(
        int anio, int mes, int dia, SemaforoCaducidad esperado)
    {
        CalculadoraCaducidad.Calcular(new DateOnly(anio, mes, dia), Hoy)
            .Should().Be(esperado);
    }

    [Theory]
    [InlineData(2026, 8, 11, 1)]    // mes exacto
    [InlineData(2026, 8, 10, 0)]    // un día antes del mes completo
    [InlineData(2026, 8, 12, 1)]    // un día después sigue siendo 1 completo
    [InlineData(2026, 7, 11, 0)]    // hoy
    [InlineData(2026, 6, 11, -1)]   // caducó hace 1 mes exacto
    [InlineData(2026, 6, 20, 0)]    // caducó hace menos de un mes → 0 (TIMESTAMPDIFF)
    [InlineData(2027, 7, 11, 12)]   // un año
    public void MesesCompletosRestantes_SemanticaTimestampDiff(
        int anio, int mes, int dia, int esperado)
    {
        CalculadoraCaducidad.MesesCompletosRestantes(new DateOnly(anio, mes, dia), Hoy)
            .Should().Be(esperado);
    }

    [Fact]
    public void DiasRestantes_NegativoSiYaCaduco()
    {
        CalculadoraCaducidad.DiasRestantes(new DateOnly(2026, 7, 1), Hoy).Should().Be(-10);
        CalculadoraCaducidad.DiasRestantes(new DateOnly(2026, 7, 21), Hoy).Should().Be(10);
    }
}
