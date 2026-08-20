using FluentAssertions;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Edad del paciente. Todo con fecha "hoy" explícita: un test que dependa de
/// la fecha real del reloj falla solo el día del cumpleaños, que es justo el
/// día en que uno no está mirando.
/// </summary>
public class EdadPacienteTests
{
    private static DateOnly F(int a, int m, int d) => new(a, m, d);

    // ---------- Años ----------

    [Fact]
    public void EnAnios_CumpleanosYaPaso_CuentaElAnioCompleto()
    {
        EdadPaciente.EnAnios(F(1990, 3, 15), F(2026, 8, 12)).Should().Be(36);
    }

    [Fact]
    public void EnAnios_CumpleanosTodaviaNoLlega_RestaUnAnio()
    {
        EdadPaciente.EnAnios(F(1990, 12, 25), F(2026, 8, 12)).Should().Be(35);
    }

    [Fact]
    public void EnAnios_JustoElDiaDelCumpleanos_YaLosCumplio()
    {
        EdadPaciente.EnAnios(F(1990, 8, 12), F(2026, 8, 12)).Should().Be(36);
    }

    [Fact]
    public void EnAnios_UnDiaAntesDelCumpleanos_TodaviaNo()
    {
        EdadPaciente.EnAnios(F(1990, 8, 13), F(2026, 8, 12)).Should().Be(35);
    }

    [Fact]
    public void EnAnios_NacidoEl29DeFebrero_CumpleEl28EnAnioNoBisiesto()
    {
        // Convención de .NET (AddYears manda 29-feb al 28). Es la que se usa
        // también en los documentos: no se deja al paciente sin cumpleaños.
        EdadPaciente.EnAnios(F(2004, 2, 29), F(2025, 2, 28)).Should().Be(21);
        EdadPaciente.EnAnios(F(2004, 2, 29), F(2025, 2, 27)).Should().Be(20);
    }

    [Fact]
    public void EnAnios_SinFecha_DevuelveNull()
    {
        EdadPaciente.EnAnios(null, F(2026, 8, 12)).Should().BeNull();
    }

    [Fact]
    public void EnAnios_FechaFutura_DevuelveNull()
    {
        // Dato mal cargado. Mejor no mostrar nada que mostrar "-3 años".
        EdadPaciente.EnAnios(F(2030, 1, 1), F(2026, 8, 12)).Should().BeNull();
    }

    // ---------- Texto ----------

    [Fact]
    public void Texto_Adulto_DiceAnios()
    {
        EdadPaciente.Texto(F(1990, 3, 15), F(2026, 8, 12)).Should().Be("36 años");
    }

    [Fact]
    public void Texto_UnAnioExacto_EnSingular()
    {
        EdadPaciente.Texto(F(2025, 8, 12), F(2026, 8, 12)).Should().Be("1 año");
    }

    [Fact]
    public void Texto_MenorDeUnAnio_DiceMeses()
    {
        // Lo que la recepción necesita oír de un bebé: no "0 años".
        EdadPaciente.Texto(F(2026, 1, 12), F(2026, 8, 12)).Should().Be("7 meses");
    }

    [Fact]
    public void Texto_UnMesExacto_EnSingular()
    {
        EdadPaciente.Texto(F(2026, 7, 12), F(2026, 8, 12)).Should().Be("1 mes");
    }

    [Fact]
    public void Texto_MesTodaviaNoCumplido_NoLoCuenta()
    {
        // Del 20 de julio al 12 de agosto no hay un mes cumplido: son 23 días.
        EdadPaciente.Texto(F(2026, 7, 20), F(2026, 8, 12)).Should().Be("23 días");
    }

    [Fact]
    public void Texto_MenorDeUnMes_DiceDias()
    {
        EdadPaciente.Texto(F(2026, 8, 1), F(2026, 8, 12)).Should().Be("11 días");
    }

    [Fact]
    public void Texto_UnDia_EnSingular()
    {
        EdadPaciente.Texto(F(2026, 8, 11), F(2026, 8, 12)).Should().Be("1 día");
    }

    [Fact]
    public void Texto_NacidoHoy_DiceRecienNacido()
    {
        EdadPaciente.Texto(F(2026, 8, 12), F(2026, 8, 12)).Should().Be("Recién nacido");
    }

    [Fact]
    public void Texto_SinFechaOFutura_DevuelveGuion()
    {
        EdadPaciente.Texto(null, F(2026, 8, 12)).Should().Be("—");
        EdadPaciente.Texto(F(2030, 1, 1), F(2026, 8, 12)).Should().Be("—");
    }

    [Fact]
    public void Texto_CruzandoElAnio_CuentaLosMesesBien()
    {
        // Nació en noviembre, hoy es enero: dos meses, no "-10".
        EdadPaciente.Texto(F(2025, 11, 20), F(2026, 1, 25)).Should().Be("2 meses");
    }
}
