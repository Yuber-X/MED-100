using FluentAssertions;
using MED100.Models;
using MED100.Services;

namespace MED100.Services.Tests;

/// <summary>
/// Las tres cuentas de la factura de clínica. Las tres mueven plata y las tres
/// se congelan en la factura al emitirla, así que un error acá no se descubre
/// hasta que alguien reclama meses después.
/// </summary>
public class CalculosClinicaTests
{
    private static VentaLinea Consulta(decimal precio = 1500m, int cantidad = 1) =>
        VentaLinea.DeProcedimiento(1, "Consulta general", cantidad, precio);

    private static VentaLinea Insumo(decimal precio = 100m, int cantidad = 1) =>
        VentaLinea.DeInsumo(10, "Gasa estéril", cantidad, precio);

    // =========================================================
    // ITBIS: la diferencia de fondo con un POS común
    // =========================================================

    [Fact]
    public void Totales_SoloProcedimientos_NoLlevanItbis()
    {
        // Los servicios de salud están EXENTOS en RD. Si esto cobrara 18%,
        // cada consulta saldría 270 pesos más cara de lo que debe.
        var totales = CalculosClinica.CalcularTotales([Consulta()], 18m, ModoRedondeo.Centavo);

        totales.Subtotal.Should().Be(1500m);
        totales.BaseGravada.Should().Be(0m);
        totales.Itbis.Should().Be(0m);
        totales.Total.Should().Be(1500m);
    }

    [Fact]
    public void Totales_SoloInsumos_LlevanItbisCompleto()
    {
        var totales = CalculosClinica.CalcularTotales([Insumo(100m, 2)], 18m, ModoRedondeo.Centavo);

        totales.Subtotal.Should().Be(200m);
        totales.BaseGravada.Should().Be(200m);
        totales.Itbis.Should().Be(36m);
        totales.Total.Should().Be(236m);
    }

    [Fact]
    public void Totales_Mezclados_ElItbisSaleSoloDeLosInsumos()
    {
        // Consulta 1500 (exenta) + gasa 100 (gravada) → ITBIS = 18 sobre 100,
        // NO 288 sobre 1600. Es el caso que rompe si se copia el POS-500.
        var totales = CalculosClinica.CalcularTotales(
            [Consulta(), Insumo()], 18m, ModoRedondeo.Centavo);

        totales.Subtotal.Should().Be(1600m);
        totales.BaseGravada.Should().Be(100m);
        totales.BaseExenta.Should().Be(1500m);
        totales.Itbis.Should().Be(18m);
        totales.Total.Should().Be(1618m);
    }

    [Fact]
    public void Totales_ProcedimientoGravado_SiCuenta()
    {
        // La exención se decide por LÍNEA, no por tipo: si el contador dice que
        // ese servicio lleva ITBIS, lleva ITBIS.
        var gravado = VentaLinea.DeProcedimiento(1, "Servicio gravado", 1, 1000m, exento: false);

        var totales = CalculosClinica.CalcularTotales([gravado], 18m, ModoRedondeo.Centavo);

        totales.BaseGravada.Should().Be(1000m);
        totales.Itbis.Should().Be(180m);
    }

    [Fact]
    public void Totales_ElItbisSaleDeLaSuma_NoLineaPorLinea()
    {
        // Tres insumos de 33.33: la suma es 99.99 → ITBIS 18.00. Redondeando
        // línea por línea daría 6.00 × 3 = 18.00 acá, pero con otros números se
        // desvía; la regla es sumar primero y redondear una sola vez.
        var lineas = new[]
        {
            VentaLinea.DeInsumo(1, "A", 1, 33.33m),
            VentaLinea.DeInsumo(2, "B", 1, 33.33m),
            VentaLinea.DeInsumo(3, "C", 1, 33.33m)
        };

        var totales = CalculosClinica.CalcularTotales(lineas, 18m, ModoRedondeo.Centavo);

        totales.BaseGravada.Should().Be(99.99m);
        totales.Itbis.Should().Be(18.00m);   // 99.99 × 0.18 = 17.9982 → 18.00
    }

    [Fact]
    public void Totales_ConItbisApagado_NoCobraNadaAunqueHayaInsumos()
    {
        var totales = CalculosClinica.CalcularTotales([Insumo()], 0m, ModoRedondeo.Centavo);

        totales.Itbis.Should().Be(0m);
        totales.Total.Should().Be(100m);
    }

    // =========================================================
    // Honorario del médico
    // =========================================================

    [Fact]
    public void Honorario_SaleSoloDeLosProcedimientos()
    {
        // Al médico no le toca porcentaje de la gasa: esa la compró la clínica.
        var honorario = CalculosClinica.CalcularHonorario(
            [Consulta(1500m), Insumo(500m)], 7, "Dr. Ramírez", 40m);

        honorario.Base.Should().Be(1500m);
        honorario.Monto.Should().Be(600m);
    }

    [Fact]
    public void Honorario_FacturaDePurosInsumos_EsCero()
    {
        var honorario = CalculosClinica.CalcularHonorario([Insumo(500m)], 7, "Dr. Ramírez", 40m);

        honorario.Base.Should().Be(0m);
        honorario.Monto.Should().Be(0m);
    }

    [Fact]
    public void Honorario_SinPorcentaje_EsCero()
    {
        var honorario = CalculosClinica.CalcularHonorario([Consulta()], 7, "Dr. Ramírez", 0m);

        honorario.Monto.Should().Be(0m);
    }

    [Fact]
    public void Honorario_ConDecimales_RedondeaAlCentavo()
    {
        // 1333.33 × 33.33% = 444.399889 → 444.40
        var honorario = CalculosClinica.CalcularHonorario(
            [Consulta(1333.33m)], 7, "Dr. Ramírez", 33.33m);

        honorario.Monto.Should().Be(444.40m);
    }

    [Fact]
    public void Honorario_EnElMedioCentavo_RedondeaHaciaArriba()
    {
        // 250.50 × 45% = 112.725 exacto. Con AwayFromZero (la regla del
        // proyecto) da 112.73; con el redondeo bancario de .NET daría 112.72.
        // Este es el test que distingue los dos y el que se rompe si alguien
        // cambia el MidpointRounding.
        var honorario = CalculosClinica.CalcularHonorario(
            [Consulta(250.50m)], 7, "Dr. Ramírez", 45m);

        honorario.Monto.Should().Be(112.73m);
    }

    [Fact]
    public void Honorario_ConservaElPorcentajeQueSeLePaso()
    {
        // Es el que se copia a la factura. Que salga de acá y no del catálogo
        // es lo que impide que subirle el porcentaje mañana reescriba el pasado.
        var honorario = CalculosClinica.CalcularHonorario([Consulta()], 7, "Dr. Ramírez", 45m);

        honorario.Porcentaje.Should().Be(45m);
        honorario.MedicoId.Should().Be(7);
        honorario.MedicoNombre.Should().Be("Dr. Ramírez");
    }

    // =========================================================
    // Reparto con la ARS
    // =========================================================

    [Fact]
    public void Reparto_SinArs_PagaTodoElPaciente()
    {
        var reparto = CalculosClinica.CalcularReparto(1500m, null, null, null, 0m);

        reparto.ArsId.Should().BeNull();
        reparto.Cubierto.Should().Be(0m);
        reparto.PacientePaga.Should().Be(1500m);
    }

    [Fact]
    public void Reparto_ConArs_SeparaLasDosPartes()
    {
        var reparto = CalculosClinica.CalcularReparto(1500m, 3, "ARS Humano", "AUT-99", 1200m);

        reparto.Cubierto.Should().Be(1200m);
        reparto.PacientePaga.Should().Be(300m);
        reparto.Autorizacion.Should().Be("AUT-99");
    }

    [Fact]
    public void Reparto_CubiertoMasPacienteSiempreDaElTotal()
    {
        foreach (var cubierto in new[] { 0m, 1m, 749.99m, 1500m })
        {
            var reparto = CalculosClinica.CalcularReparto(1500m, 3, "ARS", null, cubierto);
            (reparto.Cubierto + reparto.PacientePaga).Should().Be(1500m,
                $"con {cubierto} cubierto la suma tiene que cerrar");
        }
    }

    [Fact]
    public void Reparto_CubiertoMayorQueElTotal_SeRecorta()
    {
        // Dedazo típico: 5000 en una factura de 1500. Sin el recorte el
        // paciente pagaría −3500 y el cuadre del día saldría negativo.
        var reparto = CalculosClinica.CalcularReparto(1500m, 3, "ARS", null, 5000m);

        reparto.Cubierto.Should().Be(1500m);
        reparto.PacientePaga.Should().Be(0m);
    }

    [Fact]
    public void Reparto_CubiertoNegativo_SeNiega()
    {
        var accion = () => CalculosClinica.CalcularReparto(1500m, 3, "ARS", null, -100m);

        accion.Should().Throw<ArgumentException>().WithMessage("*no puede ser negativo*");
    }

    [Fact]
    public void Reparto_ArsQueCubreTodo_ElPacienteNoPagaNada()
    {
        var reparto = CalculosClinica.CalcularReparto(1500m, 3, "SeNaSa", "AUT-1", 1500m);

        reparto.PacientePaga.Should().Be(0m);
    }

    // =========================================================
    // Rebaja de precio (pedido de la clínica 2026-08-27)
    //
    // La rebaja NO es una resta al final: es un precio más bajo en la línea.
    // De ahí sale que no haya que decidir si va antes o después del ITBIS ni
    // cómo repartirla entre lo exento y lo gravado — las dos preguntas que
    // hacen ambiguo un "descuento global".
    // =========================================================

    private static VentaLinea ConsultaRebajada(decimal cobrado, decimal lista, int cantidad = 1) =>
        new(null, "Consulta general", cantidad, cobrado, Exento: true,
            ProcedimientoId: 1, PrecioCatalogo: lista);

    private static VentaLinea InsumoRebajado(decimal cobrado, decimal lista, int cantidad = 1) =>
        new(10, "Gasa estéril", cantidad, cobrado, Exento: false,
            ProcedimientoId: null, PrecioCatalogo: lista);

    [Fact]
    public void Rebaja_SinTocarElPrecio_NoHayDescuento()
    {
        var totales = CalculosClinica.CalcularTotales([Consulta()], 18m, ModoRedondeo.Centavo);

        totales.Descuento.Should().Be(0m);
        totales.HuboRebaja.Should().BeFalse();
    }

    [Fact]
    public void Rebaja_SeCobraElPrecioRebajado_NoElDeLista()
    {
        var totales = CalculosClinica.CalcularTotales(
            [ConsultaRebajada(cobrado: 4000m, lista: 5000m)], 18m, ModoRedondeo.Centavo);

        totales.Subtotal.Should().Be(4000m, "el subtotal es lo que se cobra");
        totales.Total.Should().Be(4000m);
        totales.Descuento.Should().Be(1000m);
        totales.SubtotalSinRebaja.Should().Be(5000m, "es lo que se imprime arriba del descuento");
    }

    [Fact]
    public void Rebaja_SeMultiplicaPorLaCantidad()
    {
        var totales = CalculosClinica.CalcularTotales(
            [ConsultaRebajada(cobrado: 4000m, lista: 5000m, cantidad: 3)], 18m, ModoRedondeo.Centavo);

        totales.Descuento.Should().Be(3000m);
        totales.Subtotal.Should().Be(12_000m);
    }

    /// <summary>
    /// LA razón de hacerlo por línea. El ITBIS sale del precio REBAJADO: cobrar
    /// el impuesto sobre el precio de lista le haría pagar al paciente 18% de
    /// una plata que nadie le cobró.
    /// </summary>
    [Fact]
    public void Rebaja_ElItbisSaleDelPrecioQueSeCOBRA()
    {
        var totales = CalculosClinica.CalcularTotales(
            [InsumoRebajado(cobrado: 80m, lista: 100m)], 18m, ModoRedondeo.Centavo);

        totales.BaseGravada.Should().Be(80m);
        totales.Itbis.Should().Be(14.40m, "18% de 80, no de 100");
        totales.Total.Should().Be(94.40m);
    }

    /// <summary>
    /// Con exento y gravado mezclados no hay nada que repartir: cada línea ya
    /// lleva su propio precio. Es el caso que un descuento global obligaría a
    /// prorratear, y prorratear mal mueve el ITBIS.
    /// </summary>
    [Fact]
    public void Rebaja_ConExentoYGravadoMezclados_CadaUnoVaPorSuCuenta()
    {
        var totales = CalculosClinica.CalcularTotales(
            [ConsultaRebajada(cobrado: 1200m, lista: 1500m),
             InsumoRebajado(cobrado: 80m, lista: 100m)],
            18m, ModoRedondeo.Centavo);

        totales.Subtotal.Should().Be(1280m);
        totales.BaseGravada.Should().Be(80m, "la consulta sigue exenta aunque se rebaje");
        totales.Itbis.Should().Be(14.40m);
        totales.Descuento.Should().Be(320m, "300 de la consulta + 20 de la gasa");
    }

    /// <summary>
    /// Cobrar MÁS que el precio de lista no es un descuento negativo. Sin este
    /// tope, un recargo restaría del descuento de otra línea y el ticket
    /// mostraría una rebaja menor de la que se hizo.
    /// </summary>
    [Fact]
    public void Rebaja_CobrarPorEncimaDelDeLista_NoEsUnDescuentoNegativo()
    {
        var totales = CalculosClinica.CalcularTotales(
            [ConsultaRebajada(cobrado: 6000m, lista: 5000m)], 18m, ModoRedondeo.Centavo);

        totales.Descuento.Should().Be(0m);
        totales.Subtotal.Should().Be(6000m, "se cobra lo que se puso");
    }

    /// <summary>
    /// El honorario del médico también sale de lo REBAJADO. Si saliera del
    /// precio de lista, la clínica pagaría porcentaje sobre plata que no
    /// entró — y con una rebaja grande podría pagar más de lo que cobró.
    /// </summary>
    [Fact]
    public void Rebaja_ElHonorarioDelMedicoSaleDeLoQueSeCOBRA()
    {
        var honorario = CalculosClinica.CalcularHonorario(
            [ConsultaRebajada(cobrado: 4000m, lista: 5000m)], 7, "Dra. Almonte", 40m);

        honorario.Base.Should().Be(4000m);
        honorario.Monto.Should().Be(1600m, "40% de 4,000 y no de 5,000");
    }
}
