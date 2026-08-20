using FluentAssertions;
using MED100.Common;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// La llave del producto.
///
/// La llave real NO está acá a propósito: en el binario solo viaja su SHA-256
/// y en el código fuente no debe viajar ni eso. Lo que sí se prueba es todo el
/// alrededor —normalización, formato, rechazos— y, si se exporta la variable
/// de entorno MED100_LLAVE, también que esa llave abre.
/// </summary>
public class CodigoLicenciaTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MED1-AAAAA-AAAAA-AAAAA")]
    [InlineData("MED1AAAAAAAAAAAAAA")]
    [InlineData("cualquier cosa")]
    [InlineData("0000-00000-00000-00000")]
    public void EsValido_RechazaLoQueNoEsLaLlave(string? codigo)
    {
        CodigoLicencia.EsValido(codigo).Should().BeFalse();
    }

    [Theory]
    [InlineData("med1-abcde-fghjk-lmnpq", "MED1ABCDEFGHJKLMNPQ")]
    [InlineData("  MED1 ABCDE FGHJK LMNPQ  ", "MED1ABCDEFGHJKLMNPQ")]
    [InlineData("MED1_ABCDE/FGHJK.LMNPQ", "MED1ABCDEFGHJKLMNPQ")]
    public void Normalizar_DejaSoloAlfanumericosEnMayuscula(string entrada, string esperado)
    {
        CodigoLicencia.Normalizar(entrada).Should().Be(esperado);
    }

    [Fact]
    public void Normalizar_VacioCuandoNoHayNada()
    {
        CodigoLicencia.Normalizar(null).Should().BeEmpty();
        CodigoLicencia.Normalizar("---").Should().BeEmpty();
    }

    [Theory]
    [InlineData("MED1ABCDEFGHJKLMNPQ", "MED1-ABCDE-FGHJK-LMNPQ")]
    [InlineData("MED1ABC", "MED1-ABC")]
    [InlineData("MED1", "MED1")]
    [InlineData("MED1ABCDEFGHJKLMNPQXY", "MED1-ABCDE-FGHJK-LMNPQ-XY")]
    public void Formatear_PoneLosGuionesEnSuLugar(string entrada, string esperado)
    {
        CodigoLicencia.Formatear(entrada).Should().Be(esperado);
    }

    [Fact]
    public void Formatear_EsIdempotente()
    {
        var unaVez = CodigoLicencia.Formatear("MED1ABCDEFGHJKLMNPQ");
        CodigoLicencia.Formatear(unaVez).Should().Be(unaVez);
    }

    [Fact]
    public void Largo_CoincideConElFormatoDeLaLlave()
    {
        // MED1 + 5 + 5 + 5
        CodigoLicencia.Largo.Should().Be(19);
        CodigoLicencia.Normalizar("MED1-ABCDE-FGHJK-LMNPQ").Length.Should().Be(CodigoLicencia.Largo);
    }

    [Fact]
    public void AlfabetoLlave_NoTieneLosCaracteresQueSeConfundenAlDictar()
    {
        CodigoLicencia.AlfabetoLlave.Should().NotContainAny("0", "1", "I", "O");
    }

    [Fact]
    public void HashDe_EsEstableYNoDependeDelFormato()
    {
        var conGuiones = CodigoLicencia.HashDe("MED1-ABCDE-FGHJK-LMNPQ");
        var pegado = CodigoLicencia.HashDe("med1abcdefghjklmnpq");

        conGuiones.Should().Be(pegado);
        conGuiones.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
    }

    /// <summary>
    /// Solo corre si se exporta MED100_LLAVE con la llave de venta. Sirve para
    /// comprobar, antes de entregar, que la llave impresa en los papeles es la
    /// que este binario acepta, sin dejar la llave escrita en el repositorio.
    /// </summary>
    [Fact]
    public void EsValido_AceptaLaLlaveDeVentaSiSeIndica()
    {
        var llave = Environment.GetEnvironmentVariable("MED100_LLAVE");
        if (string.IsNullOrWhiteSpace(llave))
            return;

        CodigoLicencia.EsValido(llave).Should().BeTrue(
            "la llave de MED100_LLAVE debería estar en la lista de hashes de CodigoLicencia");
    }
}

/// <summary>La regla del demo de 15 días, sin base ni reloj de por medio.</summary>
public class CalculadoraLicenciaTests
{
    private static readonly DateTime Instalacion = new(2026, 8, 18, 15, 0, 0, DateTimeKind.Utc);

    private static LicenciaGuardada Demo(DateTime? ultimaApertura = null) =>
        new(Instalacion, Activada: false, ultimaApertura ?? Instalacion);

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 14)]
    [InlineData(13, 2)]
    [InlineData(14, 1)]
    public void Evaluar_DemoCuentaLosDiasQueQuedan(int diasPasados, int restantes)
    {
        var r = CalculadoraLicencia.Evaluar(Demo(), Instalacion.AddDays(diasPasados));

        r.Estado.Should().Be(EstadoLicencia.Demo);
        r.DiasRestantes.Should().Be(restantes);
        r.PermiteEntrar.Should().BeTrue();
    }

    [Fact]
    public void Evaluar_ElDiaSeAcabaALaMismaHoraEnQueSeInstalo()
    {
        // 14 días y 23 horas: todavía es el último día
        CalculadoraLicencia.Evaluar(Demo(), Instalacion.AddDays(14).AddHours(23))
            .Estado.Should().Be(EstadoLicencia.Demo);

        // 15 días exactos: se acabó
        CalculadoraLicencia.Evaluar(Demo(), Instalacion.AddDays(15))
            .Estado.Should().Be(EstadoLicencia.Vencida);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(400)]
    public void Evaluar_VencidaNoDejaEntrar(int diasPasados)
    {
        var r = CalculadoraLicencia.Evaluar(Demo(), Instalacion.AddDays(diasPasados));

        r.Estado.Should().Be(EstadoLicencia.Vencida);
        r.DiasRestantes.Should().Be(0);
        r.PermiteEntrar.Should().BeFalse();
    }

    [Fact]
    public void Evaluar_ActivadaGanaSobreTodoLoDemas()
    {
        // Vencidísima y con el reloj dos años atrás: aun así entra, porque pagó
        var guardada = new LicenciaGuardada(Instalacion, Activada: true, Instalacion.AddDays(300));

        var r = CalculadoraLicencia.Evaluar(guardada, Instalacion.AddYears(-2));

        r.Estado.Should().Be(EstadoLicencia.Completa);
        r.PermiteEntrar.Should().BeTrue();
        r.Etiqueta.Should().BeEmpty();
    }

    [Fact]
    public void Evaluar_DetectaElRelojEchadoParaAtras()
    {
        var guardada = Demo(ultimaApertura: Instalacion.AddDays(10));

        var r = CalculadoraLicencia.Evaluar(guardada, Instalacion.AddDays(2));

        r.Estado.Should().Be(EstadoLicencia.RelojAtrasado);
        r.PermiteEntrar.Should().BeFalse();
    }

    [Fact]
    public void Evaluar_PerdonaHastaUnDiaDeDesfase()
    {
        var guardada = Demo(ultimaApertura: Instalacion.AddDays(10));

        // 23 horas atrás: puede ser Windows corrigiendo la hora. Pasa.
        CalculadoraLicencia.Evaluar(guardada, Instalacion.AddDays(10).AddHours(-23))
            .Estado.Should().Be(EstadoLicencia.Demo);

        // 25 horas atrás: alguien movió la fecha
        CalculadoraLicencia.Evaluar(guardada, Instalacion.AddDays(10).AddHours(-25))
            .Estado.Should().Be(EstadoLicencia.RelojAtrasado);
    }

    [Fact]
    public void Evaluar_InicioEnElFuturoSeTomaComoRecienInstalada()
    {
        // Instaló con el reloj adelantado y después lo corrigieron
        var guardada = new LicenciaGuardada(Instalacion.AddDays(30), Activada: false, Instalacion);

        var r = CalculadoraLicencia.Evaluar(guardada, Instalacion);

        r.Estado.Should().Be(EstadoLicencia.Demo);
        r.DiasRestantes.Should().Be(CalculadoraLicencia.DiasDemo);
    }

    [Theory]
    [InlineData(12, "DEMO · 12 días")]
    [InlineData(1, "DEMO · último día")]
    public void Etiqueta_DiceLoQueVaEnLaPastillaDelMenu(int dias, string esperado)
    {
        new ResultadoLicencia(EstadoLicencia.Demo, dias).Etiqueta.Should().Be(esperado);
    }

    [Fact]
    public void Etiqueta_DeLosEstadosQueNoDejanEntrar()
    {
        new ResultadoLicencia(EstadoLicencia.Vencida, 0).Etiqueta.Should().Be("DEMO VENCIDO");
        new ResultadoLicencia(EstadoLicencia.RelojAtrasado, 0).Etiqueta.Should().Be("FECHA ALTERADA");
    }

    [Fact]
    public void InicioReal_MandaLaFechaMasVieja()
    {
        var vieja = Instalacion;
        var nueva = Instalacion.AddDays(10);

        // Borraron la base: el ancla es más vieja y manda
        CalculadoraLicencia.InicioReal(enLaBase: nueva, enElAncla: vieja).Should().Be(vieja);

        // El ancla se reescribió después: manda la base
        CalculadoraLicencia.InicioReal(enLaBase: vieja, enElAncla: nueva).Should().Be(vieja);

        // Sin ancla: la base
        CalculadoraLicencia.InicioReal(enLaBase: vieja, enElAncla: null).Should().Be(vieja);
    }

    [Fact]
    public void BorrarLaBaseNoReiniciaElDemo()
    {
        // Instaló hace 20 días y borró la base hoy: la base cree que es de hoy
        var hoy = Instalacion.AddDays(20);
        var inicio = CalculadoraLicencia.InicioReal(enLaBase: hoy, enElAncla: Instalacion);

        CalculadoraLicencia.Evaluar(new LicenciaGuardada(inicio, false, Instalacion), hoy)
            .Estado.Should().Be(EstadoLicencia.Vencida);
    }
}

/// <summary>
/// El ancla de %ProgramData%. Se prueba en archivos temporales, nunca en la
/// ruta real: pisarla dejaría este equipo con el demo empezado hoy.
/// </summary>
public class AnclaLicenciaTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "MED100.Tests." + Guid.NewGuid().ToString("N"));

    private AnclaLicencia Ancla(string nombre = "licencia.dat") =>
        new(Path.Combine(_carpeta, nombre));

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch (Exception) { /* carpeta temporal */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Leer_NullSiTodaviaNoHayArchivo()
    {
        Ancla().Leer().Should().BeNull();
    }

    [Fact]
    public void EscribirYLeer_DevuelveLoMismo()
    {
        var ancla = Ancla();
        var inicio = new DateTime(2026, 8, 18, 15, 30, 0, DateTimeKind.Utc);

        ancla.Escribir(inicio, activada: false).Should().BeTrue();

        var leido = ancla.Leer();
        leido.Should().NotBeNull();
        leido!.Value.InicioUtc.Should().Be(inicio);
        leido.Value.InicioUtc.Kind.Should().Be(DateTimeKind.Utc);
        leido.Value.Activada.Should().BeFalse();
    }

    [Fact]
    public void Escribir_PisaLoAnteriorAlActivar()
    {
        var ancla = Ancla();
        var inicio = new DateTime(2026, 8, 18, 15, 30, 0, DateTimeKind.Utc);

        ancla.Escribir(inicio, activada: false);
        ancla.Escribir(inicio, activada: true);

        ancla.Leer()!.Value.Activada.Should().BeTrue();
    }

    [Fact]
    public void Leer_NullSiAlguienEditoElArchivoAMano()
    {
        var ancla = Ancla();
        ancla.Escribir(new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc), activada: false);

        // El clásico: abrirlo con el Bloc de notas y correr la fecha
        File.WriteAllText(ancla.Ruta, "MED100v1|638000000000000000|1");

        ancla.Leer().Should().BeNull();
    }

    [Fact]
    public void Leer_NullSiElArchivoEstaAMedioEscribir()
    {
        var ancla = Ancla();
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(ancla.Ruta, "no es base64 ni nada parecido");

        ancla.Leer().Should().BeNull();
    }

    [Fact]
    public void ElArchivoNoDejaVerLaFechaEnClaro()
    {
        var ancla = Ancla();
        ancla.Escribir(new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc), activada: false);

        File.ReadAllText(ancla.Ruta).Should().NotContain("MED100v1");
    }

    [Fact]
    public void Escribir_CreaLaCarpetaSiNoExiste()
    {
        var ancla = new AnclaLicencia(Path.Combine(_carpeta, "sub", "carpeta", "licencia.dat"));

        ancla.Escribir(DateTime.UtcNow, activada: false).Should().BeTrue();
        File.Exists(ancla.Ruta).Should().BeTrue();
    }
}
