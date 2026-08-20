using FluentAssertions;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// El código de dos letras del médico. Es lo que la gente lee en el papelito
/// de la sala, así que un error acá se ve todos los días y en voz alta.
/// </summary>
public class CodigoTurnoMedicoTests
{
    // =========================================================
    // La regla que dio Yuber: primera del nombre + última del apellido
    // =========================================================

    [Fact]
    public void Proponer_NombreCompleto_TomaLaPrimeraYLaUltima()
    {
        // El ejemplo textual del pedido: "Yuber Santana Lizardo" → YO.
        CodigoTurnoMedico.Proponer("Yuber Santana Lizardo").Should().Be("YO");
    }

    [Fact]
    public void Proponer_DosPalabras_UsaLaSegundaComoApellido()
    {
        CodigoTurnoMedico.Proponer("Ingrid Lizardo").Should().Be("IO");
    }

    [Fact]
    public void Proponer_UnaSolaPalabra_UsaEsaParaLasDosLetras()
    {
        // Mejor "ID" que dejar al médico sin código.
        CodigoTurnoMedico.Proponer("Ingrid").Should().Be("ID");
    }

    [Fact]
    public void Proponer_ConMinusculas_DevuelveMayusculas()
    {
        CodigoTurnoMedico.Proponer("yuber santana lizardo").Should().Be("YO");
    }

    [Fact]
    public void Proponer_ConAcentos_LosQuita()
    {
        // La tira térmica de 80mm no siempre tiene el juego completo:
        // una "Ñ" o una "Á" pueden salir como basura.
        CodigoTurnoMedico.Proponer("Ángel Núñez").Should().Be("AZ");
    }

    [Fact]
    public void Proponer_ConTitulo_NoSeCome_ElPunto()
    {
        // "Dr." se parte por el punto y queda "Dr" como primera palabra.
        CodigoTurnoMedico.Proponer("Dr. Pedro Sosa").Should().Be("DA");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123 456")]
    public void Proponer_SinLetras_DevuelveNulo(string? nombre)
    {
        CodigoTurnoMedico.Proponer(nombre).Should().BeNull();
    }

    // =========================================================
    // Normalizar: lo que se escribe a mano
    // =========================================================

    [Fact]
    public void Normalizar_LimpiaEspaciosYSubeAMayusculas()
    {
        CodigoTurnoMedico.Normalizar("  yo ").Should().Be("YO");
    }

    [Fact]
    public void Normalizar_QuitaSimbolos()
    {
        CodigoTurnoMedico.Normalizar("Y-O.").Should().Be("YO");
    }

    [Fact]
    public void Normalizar_DejaPasarLosNumeros()
    {
        // Las variantes YO2, YO3 tienen que sobrevivir a una reedición.
        CodigoTurnoMedico.Normalizar("yo2").Should().Be("YO2");
    }

    [Fact]
    public void Normalizar_RecortaAlLargoMaximo()
    {
        CodigoTurnoMedico.Normalizar("ABCDEFGHIJK")
            .Should().HaveLength(CodigoTurnoMedico.LargoMaximo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Normalizar_VacioODeSimbolos_EsNulo(string? codigo)
    {
        // Vacío y ausente son lo mismo: así el índice único de la base deja
        // convivir a varios médicos sin código.
        CodigoTurnoMedico.Normalizar(codigo).Should().BeNull();
    }

    // =========================================================
    // Variantes ante choque
    // =========================================================

    [Fact]
    public void Variante_PrimerIntento_EsElCodigoPelado()
    {
        CodigoTurnoMedico.Variante("YO", 1).Should().Be("YO");
    }

    [Fact]
    public void Variante_SegundoIntento_LeCuelgaElNumero()
    {
        CodigoTurnoMedico.Variante("YO", 2).Should().Be("YO2");
    }

    [Fact]
    public void Variante_NuncaPasaElLargoMaximo()
    {
        for (var intento = 1; intento <= 99; intento++)
        {
            CodigoTurnoMedico.Variante("ABCDEFGH", intento).Length
                .Should().BeLessThanOrEqualTo(CodigoTurnoMedico.LargoMaximo,
                    $"el intento {intento} no puede desbordar la columna");
        }
    }

    [Fact]
    public void Variante_NoSeRepite_EntreIntentos()
    {
        // Si dos intentos dieran el mismo código, el bucle que busca uno libre
        // se quedaría girando contra el mismo choque.
        var vistos = Enumerable.Range(1, 99)
            .Select(i => CodigoTurnoMedico.Variante("YO", i))
            .ToList();

        vistos.Should().OnlyHaveUniqueItems();
    }
}
