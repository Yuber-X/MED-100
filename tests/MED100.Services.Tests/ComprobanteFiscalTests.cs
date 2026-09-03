using FluentAssertions;
using MED100.Common;

namespace MED100.Services.Tests;

/// <summary>
/// La línea de "qué clase de comprobante es" que la clínica pidió en el
/// encabezado de la factura (2026-08-27: <i>"Factura de consumo"</i>).
///
/// Se deduce del prefijo del NCF y no se escribe fija: si mañana emiten un
/// crédito fiscal y el papel sigue diciendo "de consumo", el contador del
/// paciente no puede usarlo, y el error recién aparece en la DGII.
/// </summary>
public class ComprobanteFiscalTests
{
    [Theory]
    [InlineData("B0100000001", "Factura de crédito fiscal")]
    [InlineData("B0200000123", "Factura de consumo")]
    [InlineData("B0300000004", "Nota de débito")]
    [InlineData("B0400000007", "Nota de crédito")]
    [InlineData("B1400000002", "Factura de régimen especial")]
    [InlineData("B1500000009", "Comprobante gubernamental")]
    public void ElTipoSaleDelPrefijoDelNcf(string ncf, string esperado) =>
        ComprobanteFiscal.Tipo(ncf).Should().Be(esperado);

    /// <summary>
    /// Sin NCF no hay comprobante fiscal. Escribir "Factura de consumo" ahí
    /// sería afirmar un tipo que nadie asignó, en un papel que el paciente
    /// puede llevarle a su contador.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("B0")]
    public void SinNcfDiceFacturaASecas(string? ncf) =>
        ComprobanteFiscal.Tipo(ncf).Should().Be("Factura");

    /// <summary>
    /// Un prefijo desconocido tampoco se traduce a la fuerza: decir el tipo
    /// equivocado es peor que no decirlo.
    /// </summary>
    [Fact]
    public void UnPrefijoDesconocidoNoInventaUnTipo() =>
        ComprobanteFiscal.Tipo("Z9900000001").Should().Be("Factura");

    /// <summary>El NCF se teclea a mano: se acepta como venga.</summary>
    [Theory]
    [InlineData("b0200000123")]
    [InlineData("  B0200000123  ")]
    public void SeNormalizaLoQueSeTecleo(string ncf) =>
        ComprobanteFiscal.Tipo(ncf).Should().Be("Factura de consumo");
}
