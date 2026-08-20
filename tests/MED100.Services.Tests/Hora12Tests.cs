using FluentAssertions;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Horas de 12 con AM/PM. Parece trivial y no lo es: de acá salen los horarios
/// de los médicos, y una hora mal convertida deja a alguien "atendiendo" a
/// medianoche o hace que la agenda no ofrezca ni un hueco sin explicar por qué.
/// </summary>
public class Hora12Tests
{
    private static TimeOnly Parsear(string hora, string meridiano)
    {
        Hora12.TryParsear(hora, meridiano, out var resultado).Should().BeTrue(
            $"'{hora} {meridiano}' tiene que entenderse");
        return resultado;
    }

    // =========================================================
    // Las 12: el caso que rompe el "sumale 12 si es PM"
    // =========================================================

    [Fact]
    public void Las12PM_SonElMediodia()
    {
        Parsear("12:00", "PM").Should().Be(new TimeOnly(12, 0));
    }

    [Fact]
    public void Las12AM_SonLaMedianoche()
    {
        Parsear("12:00", "AM").Should().Be(new TimeOnly(0, 0));
    }

    [Fact]
    public void Las1230PM_SonLas1230()
    {
        Parsear("12:30", "PM").Should().Be(new TimeOnly(12, 30));
    }

    // =========================================================
    // Lo de todos los días
    // =========================================================

    [Theory]
    [InlineData("8:00", "AM", 8, 0)]
    [InlineData("08:00", "AM", 8, 0)]
    [InlineData("8", "AM", 8, 0)]
    [InlineData("5:30", "PM", 17, 30)]
    [InlineData("1:05", "PM", 13, 5)]
    [InlineData("11:59", "PM", 23, 59)]
    [InlineData("6:15", "AM", 6, 15)]
    public void HorasCorrientes_SeEntienden(string hora, string meridiano, int h24, int minuto)
    {
        Parsear(hora, meridiano).Should().Be(new TimeOnly(h24, minuto));
    }

    [Theory]
    [InlineData("am")]
    [InlineData("a.m.")]
    [InlineData("A. M.")]
    public void ElMeridiano_SeAceptaComoLoEscribeLaGente(string meridiano)
    {
        Parsear("9:00", meridiano).Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public void ElPunto_ValeComoSeparador()
    {
        // En el teclado numérico el punto queda más a mano que los dos puntos.
        Parsear("8.30", "AM").Should().Be(new TimeOnly(8, 30));
    }

    // =========================================================
    // Lo que hay que rechazar
    // =========================================================

    [Theory]
    [InlineData("", "AM")]
    [InlineData("   ", "AM")]
    [InlineData("abc", "AM")]
    [InlineData("13:00", "PM")]   // en 12h no existen las 13
    [InlineData("0:30", "AM")]    // tampoco las 0
    [InlineData("8:60", "AM")]    // ni el minuto 60
    [InlineData("8:30:45", "AM")] // segundos: no se piden
    public void LoQueNoSeEntiende_SeRechaza(string hora, string meridiano)
    {
        Hora12.TryParsear(hora, meridiano, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XM")]
    public void SinMeridianoValido_SeRechaza_EnVezDeAsumirAM(string? meridiano)
    {
        // Asumir AM dejaría a un médico entrando a medianoche sin que nadie lo
        // note hasta que la agenda no ofrezca ningún hueco por la tarde.
        Hora12.TryParsear("8:00", meridiano, out _).Should().BeFalse();
    }

    // =========================================================
    // Ida y vuelta
    // =========================================================

    [Theory]
    [InlineData(0, 0, "12:00", "AM")]
    [InlineData(8, 0, "8:00", "AM")]
    [InlineData(12, 0, "12:00", "PM")]
    [InlineData(17, 30, "5:30", "PM")]
    [InlineData(23, 59, "11:59", "PM")]
    public void Formatear_DevuelveLoQueMuestranLosDosControles(
        int h24, int minuto, string horaEsperada, string meridianoEsperado)
    {
        var (hora, meridiano) = Hora12.Formatear(new TimeOnly(h24, minuto));

        hora.Should().Be(horaEsperada);
        meridiano.Should().Be(meridianoEsperado);
    }

    [Fact]
    public void FormatearYVolverAParsear_DaLaMismaHora()
    {
        // La propiedad que de verdad importa: abrir el formulario del médico y
        // guardarlo sin tocar nada NO puede cambiarle el horario.
        for (var h = 0; h < 24; h++)
        {
            foreach (var m in new[] { 0, 1, 15, 30, 45, 59 })
            {
                var original = new TimeOnly(h, m);
                var (hora, meridiano) = Hora12.Formatear(original);

                Hora12.TryParsear(hora, meridiano, out var vuelta).Should().BeTrue(
                    $"'{hora} {meridiano}' salió de Formatear, tiene que poder volver");
                vuelta.Should().Be(original, $"la ida y vuelta de {original} tiene que cerrar");
            }
        }
    }

    [Fact]
    public void Texto_SeLeeComoEnLaCalle()
    {
        Hora12.Texto(new TimeOnly(8, 0)).Should().Contain("8:00");
        Hora12.Texto(new TimeOnly(17, 30)).Should().Contain("5:30");
    }
}
