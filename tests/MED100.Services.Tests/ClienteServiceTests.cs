using FluentAssertions;
using MED100.Services;

namespace MED100.Services.Tests;

public class ClienteServiceTests
{
    [Theory]
    [InlineData("00112345678", "001-1234567-8")]      // 11 dígitos pegados
    [InlineData("001-1234567-8", "001-1234567-8")]    // ya formateada
    [InlineData(" 001 1234567 8 ", "001-1234567-8")]  // con espacios
    public void NormalizarCedula_OnceDigitos_FormateaGuiones(string entrada, string esperado) =>
        ClienteService.NormalizarCedula(entrada).Should().Be(esperado);

    [Fact]
    public void NormalizarCedula_Pasaporte_SeAceptaTalCual() =>
        ClienteService.NormalizarCedula("PA1234567").Should().Be("PA1234567");

    [Fact]
    public void NormalizarCedula_DemasiadoLarga_Falla()
    {
        var accion = () => ClienteService.NormalizarCedula("ABCDEFGHIJKLMNOPQRSTU");
        accion.Should().Throw<ArgumentException>();
    }

    // El correo es por donde sale el recordatorio de cita: si está mal escrito
    // el envío falla de noche, en silencio, y el paciente no aparece.

    [Theory]
    [InlineData("maria@gmail.com")]
    [InlineData("maria.perez@clinica.com.do")]
    [InlineData("m+citas@sub.dominio.org")]
    public void EsEmailPlausible_CorreosNormales_PasanTodos(string email) =>
        ClienteService.EsEmailPlausible(email).Should().BeTrue();

    [Theory]
    [InlineData("maria")]              // sin arroba
    [InlineData("maria@")]             // sin dominio
    [InlineData("@gmail.com")]         // sin usuario
    [InlineData("maria@@gmail.com")]   // dos arrobas
    [InlineData("maria@gmail")]        // dominio sin punto
    [InlineData("maria@gmail.")]       // termina en punto
    [InlineData("maria perez@x.com")]  // espacio en el medio
    public void EsEmailPlausible_DedazosTipicos_SeRechazan(string email) =>
        ClienteService.EsEmailPlausible(email).Should().BeFalse();
}
