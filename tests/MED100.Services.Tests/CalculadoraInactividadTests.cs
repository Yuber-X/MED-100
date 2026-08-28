using FluentAssertions;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// La alerta que pidió la clínica el 2026-08-25: <i>"una especie de alerta
/// cuando un paciente dure más de 6 meses sin ser atendido"</i>.
///
/// Lo que se prueba es la regla de a quién se llama. Un falso positivo hace
/// perder tiempo llamando a alguien que vino la semana pasada; un falso
/// negativo deja perder al paciente, que es lo que la clínica quiere evitar.
/// </summary>
public class CalculadoraInactividadTests
{
    // Fecha fija: la regla no puede depender de cuándo se corran las pruebas.
    private static readonly DateTime Ahora = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
    private const int Corte = 6;

    [Fact]
    public void Un_paciente_que_vino_la_semana_pasada_no_esta_inactivo()
    {
        CalculadoraInactividad.DejoDeVenir(Ahora.AddDays(-7), Ahora, Corte)
            .Should().BeFalse();
    }

    [Fact]
    public void Un_paciente_que_no_viene_hace_ocho_meses_esta_inactivo()
    {
        CalculadoraInactividad.DejoDeVenir(Ahora.AddMonths(-8), Ahora, Corte)
            .Should().BeTrue();
    }

    /// <summary>
    /// La frontera exacta. "Más de 6 meses" es estricto: justo a los 6 meses
    /// todavía NO se marca; un día después, sí. Sin fijar esto, un cambio de
    /// signo pasaría inadvertido y movería la lista de llamados un mes entero.
    /// </summary>
    [Fact]
    public void La_frontera_de_los_seis_meses_es_estricta()
    {
        CalculadoraInactividad.DejoDeVenir(Ahora.AddMonths(-Corte), Ahora, Corte)
            .Should().BeFalse("justo a los 6 meses todavía no se cumplió MÁS de 6");

        CalculadoraInactividad.DejoDeVenir(Ahora.AddMonths(-Corte).AddDays(-1), Ahora, Corte)
            .Should().BeTrue("un día después ya son más de 6 meses");
    }

    /// <summary>
    /// El que nunca vino NO es un paciente perdido: es uno nuevo. Si contara,
    /// la lista de a quién llamar se llenaría de gente que nunca pisó la
    /// clínica y dejaría de ser útil.
    /// </summary>
    [Fact]
    public void El_que_nunca_vino_no_cuenta_como_inactivo()
    {
        CalculadoraInactividad.DejoDeVenir(null, Ahora, Corte).Should().BeFalse();
        CalculadoraInactividad.Describir(null, Ahora, Corte).Should().BeEmpty();
    }

    /// <summary>
    /// Un corte de cero marcaría a TODO el mundo apenas sale por la puerta.
    /// La pantalla ya lo acota, pero la regla no puede depender de eso.
    /// </summary>
    [Fact]
    public void Un_corte_de_cero_o_negativo_no_marca_a_nadie()
    {
        CalculadoraInactividad.DejoDeVenir(Ahora.AddYears(-5), Ahora, 0).Should().BeFalse();
        CalculadoraInactividad.DejoDeVenir(Ahora.AddYears(-5), Ahora, -3).Should().BeFalse();
    }

    [Theory]
    [InlineData(7, "7 meses sin venir")]
    [InlineData(12, "1 año sin venir")]
    [InlineData(14, "1 año y 2 meses sin venir")]
    [InlineData(24, "2 años sin venir")]
    [InlineData(25, "2 años y 1 mes sin venir")]
    public void El_texto_dice_cuanto_hace_en_castellano(int meses, string esperado)
    {
        CalculadoraInactividad.Describir(Ahora.AddMonths(-meses), Ahora, Corte)
            .Should().Be(esperado);
    }

    /// <summary>
    /// Los meses se cuentan por CALENDARIO, no dividiendo días entre 30. Con la
    /// división, alguien que vino hace exactamente 6 meses caía a veces en 5 y
    /// el texto contradecía a la marca de la fila.
    /// </summary>
    [Fact]
    public void Los_meses_se_cuentan_por_calendario()
    {
        // Del 28-feb al 28-ago hay 6 meses justos, aunque sean 181 días
        var febrero = new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        CalculadoraInactividad.MesesSinVenir(febrero, Ahora).Should().Be(6);

        // Un día antes de cumplir el mes todavía no cuenta
        CalculadoraInactividad.MesesSinVenir(Ahora.AddMonths(-3).AddDays(1), Ahora).Should().Be(2);
    }

    [Fact]
    public void Una_visita_futura_no_da_meses_negativos()
    {
        CalculadoraInactividad.MesesSinVenir(Ahora.AddMonths(2), Ahora).Should().Be(0);
        CalculadoraInactividad.DejoDeVenir(Ahora.AddMonths(2), Ahora, Corte).Should().BeFalse();
    }
}
