using FluentAssertions;
using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using MED100.Services;

namespace MED100.Data.Tests;

/// <summary>
/// Facturación de clínica contra MySQL real.
///
/// Lo que se prueba de verdad:
///  - que el ITBIS salga SOLO de los insumos (los servicios de salud van exentos);
///  - que el porcentaje del honorario quede CONGELADO en la factura;
///  - que lo que cubre la ARS no se confunda con lo que paga el paciente;
///  - que cobrar una cita la deje unida a su factura.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public class FacturacionClinicaTests : IAsyncLifetime
{
    private const string CadenaServidor = "Server=localhost;Port=3306;Uid=root;Pwd=root;";
    private const string CadenaTest = CadenaServidor + "Database=med100_facturacion_test;";

    private ConexionFactory _factory = null!;
    private VentaService _ventas = null!;
    private FacturaService _facturas = null!;
    private MedicoRepository _medicosRepo = null!;
    private ConfiguracionNegocioService _config = null!;

    private long _pacienteId, _medicoId, _procedimientoId, _insumoId, _arsId, _usuarioId;

    public async Task InitializeAsync()
    {
        await using (var conexion = new MySqlConnection(CadenaServidor))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_facturacion_test;");
        }
        await new VerificadorBaseDatos(CadenaTest).CrearEsquemaAsync();

        _factory = new ConexionFactory(CadenaTest);
        var auditoria = new AuditoriaService(new AuditoriaRepository(_factory));
        var facturaRepo = new FacturaRepository(_factory);
        _medicosRepo = new MedicoRepository(_factory);
        _config = new ConfiguracionNegocioService(new ConfiguracionNegocioRepository(_factory), auditoria);
        await _config.CargarAsync();

        _ventas = new VentaService(facturaRepo, new ClienteRepository(_factory),
            _medicosRepo, new ArsRepository(_factory), _config, auditoria);
        _facturas = new FacturaService(facturaRepo, auditoria);

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            _usuarioId = await InsertarAsync(conexion, """
                INSERT INTO usuario (username, password_hash, nombre, rol_id)
                VALUES ('test', 'hash', 'Recepción', (SELECT id FROM rol WHERE nombre='Admin'));
                """);
            _pacienteId = await InsertarAsync(conexion,
                "INSERT INTO cliente (nombre) VALUES ('María Pérez');");
            _medicoId = await InsertarAsync(conexion,
                "INSERT INTO medico (nombre, porcentaje_honorario) VALUES ('Dr. Ramírez', 40.00);");
            _procedimientoId = await InsertarAsync(conexion, """
                INSERT INTO procedimiento (nombre, precio, duracion_minutos, exento_itbis)
                VALUES ('Consulta general', 1500.00, 30, 1);
                """);
            _insumoId = await InsertarAsync(conexion,
                "INSERT INTO producto (nombre, precio, cantidad) VALUES ('Gasa estéril', 100.00, 50);");
            // "ARS Humano" ya viene sembrada en 002_seed_data.sql: se busca en
            // vez de insertarla, o choca con uq_ars_nombre.
            await using var cmdArs = conexion.CreateCommand();
            cmdArs.CommandText = "SELECT id FROM ars WHERE nombre = 'ARS Humano';";
            _arsId = Convert.ToInt64(await cmdArs.ExecuteScalarAsync());
        }

        // `precio_editar` va en la lista porque el fixture es un Admin, y en el
        // seed el Admin los tiene todos. Los dos tests de la rebaja sin permiso
        // vuelven a entrar como Cajero a propósito.
        SesionActual.Iniciar(_usuarioId, "test", "Recepción", "Admin",
            ["vender", "configuracion", "facturas_anular", "precio_editar"], DateTime.UtcNow, 1);

        // ITBIS encendido: es la única forma de comprobar que los
        // procedimientos NO lo pagan y los insumos SÍ.
        var cfg = _config.Actual;
        cfg.ItbisActivo = true;
        cfg.ItbisTasa = 18.00m;
        cfg.ArsActivo = true;
        await _config.GuardarAsync(cfg);
    }

    public async Task DisposeAsync()
    {
        SesionActual.Cerrar();
        await using var conexion = new MySqlConnection(CadenaServidor);
        await conexion.OpenAsync();
        await Ejecutar(conexion, "DROP DATABASE IF EXISTS med100_facturacion_test;");
    }

    private static async Task Ejecutar(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertarAsync(MySqlConnection conexion, string sql)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql + "\nSELECT LAST_INSERT_ID();";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private VentaLinea Consulta(decimal precio = 1500m) =>
        VentaLinea.DeProcedimiento(_procedimientoId, "Consulta general", 1, precio);

    private VentaLinea Gasa(int cantidad = 1, decimal precio = 100m) =>
        VentaLinea.DeInsumo(_insumoId, "Gasa estéril", cantidad, precio);

    private VentaSolicitud Cobro(IReadOnlyList<VentaLinea> lineas, long? medicoId = null,
        long? arsId = null, decimal cubierto = 0m, string? ncf = null, long? citaId = null,
        decimal? efectivo = null) =>
        new(lineas, _pacienteId, MetodoPagoFactura.Efectivo, efectivo ?? 100_000m,
            medicoId, arsId, arsId is null ? null : "AUT-001", cubierto, ncf, citaId);

    // =========================================================
    // ITBIS por exención de línea
    // =========================================================

    [Fact]
    public async Task Cobrar_SoloProcedimiento_NoLlevaItbis()
    {
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        r.Totales.Itbis.Should().Be(0m);
        r.Totales.Total.Should().Be(1500m);

        // Y la factura persiste itbis = 0, no un valor calculado al vuelo.
        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);
        leida!.Totales.Itbis.Should().Be(0m);
    }

    [Fact]
    public async Task Cobrar_ProcedimientoMasInsumo_ElItbisSaleSoloDelInsumo()
    {
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta(), Gasa()], _medicoId));

        r.Totales.Subtotal.Should().Be(1600m);
        r.Totales.Itbis.Should().Be(18m);     // 18% de 100, no de 1600
        r.Totales.Total.Should().Be(1618m);
    }

    [Fact]
    public async Task Cobrar_ElDetalleGuardaLaExencionDeCadaLinea()
    {
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta(), Gasa()], _medicoId));

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);

        var procedimiento = leida!.Lineas.Single(l => l.EsProcedimiento);
        var insumo = leida.Lineas.Single(l => !l.EsProcedimiento);
        procedimiento.Exento.Should().BeTrue();
        insumo.Exento.Should().BeFalse();
    }

    // =========================================================
    // Stock: solo los insumos
    // =========================================================

    [Fact]
    public async Task Cobrar_DescuentaSoloElInsumo()
    {
        await _ventas.RegistrarVentaAsync(Cobro([Consulta(), Gasa(3)], _medicoId));

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT cantidad FROM producto WHERE id = {_insumoId};";

        Convert.ToInt32(await cmd.ExecuteScalarAsync()).Should().Be(47);
    }

    [Fact]
    public async Task Cobrar_MuchosProcedimientos_NoTocaNingunStock()
    {
        // Tres sesiones del mismo procedimiento: no hay nada que descontar.
        var linea = VentaLinea.DeProcedimiento(_procedimientoId, "Consulta general", 3, 1500m);

        var accion = async () => await _ventas.RegistrarVentaAsync(Cobro([linea], _medicoId));

        await accion.Should().NotThrowAsync();
    }

    // =========================================================
    // Honorario congelado
    // =========================================================

    [Fact]
    public async Task Cobrar_GuardaElHonorarioDelMedico()
    {
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        r.Honorario!.Porcentaje.Should().Be(40m);
        r.Honorario.Monto.Should().Be(600m);

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);
        leida!.Honorario!.Monto.Should().Be(600m);
        leida.Honorario.MedicoNombre.Should().Be("Dr. Ramírez");
    }

    [Fact]
    public async Task Honorario_SubirleElPorcentajeAlMedico_NoReescribeLoYaFacturado()
    {
        // ES LA REGLA QUE NO SE NEGOCIA (CLAUDE.md §1.3.2). Si esto fallara,
        // los cuadres viejos cambiarían solos y no habría forma de saber qué se
        // le pagó al médico el mes pasado.
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        await _medicosRepo.ActualizarAsync(_medicoId,
            new MedicoDatos("Dr. Ramírez", null, null, null, null, null, 60m));

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);

        leida!.Honorario!.Porcentaje.Should().Be(40m, "el porcentaje se copió al emitir");
        leida.Honorario.Monto.Should().Be(600m);
    }

    [Fact]
    public async Task Cobrar_ConProcedimientosYSinMedico_SeNiega()
    {
        var accion = async () => await _ventas.RegistrarVentaAsync(Cobro([Consulta()]));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*Elegí el médico*");
    }

    [Fact]
    public async Task Cobrar_SoloInsumosSinMedico_SePuede()
    {
        // Vender una caja de guantes en el mostrador no necesita médico.
        var accion = async () => await _ventas.RegistrarVentaAsync(Cobro([Gasa()]));

        await accion.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Cobrar_ConMedicoInactivo_SeNiega()
    {
        await _medicosRepo.ActualizarAsync(_medicoId,
            new MedicoDatos("Dr. Ramírez", null, null, null, null, null, 40m, Activo: false));

        var accion = async () => await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*inactivo*");
    }

    // =========================================================
    // ARS
    // =========================================================

    [Fact]
    public async Task Cobrar_ConArs_SeparaLoQueCubreDeLoQuePagaElPaciente()
    {
        var r = await _ventas.RegistrarVentaAsync(
            Cobro([Consulta()], _medicoId, _arsId, cubierto: 1200m));

        r.Ars!.Cubierto.Should().Be(1200m);
        r.Ars.PacientePaga.Should().Be(300m);
        r.Ars.Autorizacion.Should().Be("AUT-001");

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);
        leida!.Ars!.Cubierto.Should().Be(1200m);
        leida.Ars.PacientePaga.Should().Be(300m);
        leida.Ars.ArsNombre.Should().Be("ARS Humano");
    }

    [Fact]
    public async Task Cobrar_SinArs_PacientePagaEsElTotal()
    {
        // Se guarda igual (no cero): es lo que permite que el cuadre sume
        // SIEMPRE paciente_paga sin preguntarse si hubo seguro.
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        await using var conexion = new MySqlConnection(CadenaTest);
        await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT paciente_paga FROM factura WHERE id = {r.FacturaId};";

        Convert.ToDecimal(await cmd.ExecuteScalarAsync()).Should().Be(1500m);
    }

    [Fact]
    public async Task Cobrar_ConArs_ElEfectivoSeComparaContraLoQuePagaElPaciente()
    {
        // La ARS cubre 1200 de 1500: con 300 en la mano alcanza. Si se
        // comparara contra el total, el mostrador rechazaría un cobro correcto.
        var accion = async () => await _ventas.RegistrarVentaAsync(
            Cobro([Consulta()], _medicoId, _arsId, cubierto: 1200m, efectivo: 300m));

        await accion.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Cobrar_EfectivoQueNoAlcanza_SeNiega()
    {
        var accion = async () => await _ventas.RegistrarVentaAsync(
            Cobro([Consulta()], _medicoId, efectivo: 500m));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*no cubre*");
    }

    [Fact]
    public async Task Cobrar_ConArsYModuloApagado_SeNiega()
    {
        var cfg = _config.Actual;
        cfg.ArsActivo = false;
        await _config.GuardarAsync(cfg);

        var accion = async () => await _ventas.RegistrarVentaAsync(
            Cobro([Consulta()], _medicoId, _arsId, cubierto: 500m));

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*apagado*");
    }

    [Fact]
    public async Task Cobrar_CubiertoSinElegirArs_SeNiega()
    {
        var solicitud = new VentaSolicitud([Consulta()], _pacienteId,
            MetodoPagoFactura.Efectivo, 100_000m, _medicoId, null, null, ArsCubierto: 500m);

        var accion = async () => await _ventas.RegistrarVentaAsync(solicitud);

        await accion.Should().ThrowAsync<ArgumentException>().WithMessage("*Elegí la ARS*");
    }

    [Fact]
    public async Task Cuadre_LoQueCubreLaArsNoEntraALaCaja()
    {
        // La regla de fondo: el seguro paga por otra vía y en otro momento.
        // Si el cuadre sumara el total, la caja siempre saldría "faltante".
        await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId, _arsId, cubierto: 1200m));

        var cuadres = new CuadreRepository(_factory);
        var resumen = await cuadres.CalcularAsync(_usuarioId, FechaNegocio.Hoy);

        resumen.TotalEfectivo.Should().Be(300m, "solo entró el copago del paciente");
        resumen.TotalVendido.Should().Be(1500m, "pero lo facturado sigue siendo el total");
    }

    // =========================================================
    // NCF
    // =========================================================

    [Fact]
    public async Task Cobrar_ConNcf_LoGuarda()
    {
        var r = await _ventas.RegistrarVentaAsync(
            Cobro([Consulta()], _medicoId, ncf: "B0200000001"));

        r.Ncf.Should().Be("B0200000001");
        (await _facturas.ObtenerCompletaAsync(r.FacturaId))!.Ncf.Should().Be("B0200000001");
    }

    [Fact]
    public async Task Cobrar_ElMismoNcfDosVeces_Falla()
    {
        // uq_factura_ncf: un comprobante fiscal no se repite. Es la restricción
        // que evita un problema con la DGII.
        await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId, ncf: "B0200000001"));

        var accion = async () => await _ventas.RegistrarVentaAsync(
            Cobro([Consulta()], _medicoId, ncf: "B0200000001"));

        await accion.Should().ThrowAsync<MySqlException>();
    }

    [Fact]
    public async Task Cobrar_SinNcf_SePuedeVariasVeces()
    {
        // La UNIQUE admite múltiples NULL: no todas las facturas llevan NCF.
        await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        var accion = async () => await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        await accion.Should().NotThrowAsync();
    }

    // =========================================================
    // Cobrar una cita
    // =========================================================

    [Fact]
    public async Task Cobrar_UnaCita_LaDejaUnidaALaFacturaYAtendida()
    {
        long citaId;
        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            citaId = await InsertarAsync(conexion, $"""
                INSERT INTO cita (cliente_id, medico_id, procedimiento_id, fecha_hora, duracion_minutos)
                VALUES ({_pacienteId}, {_medicoId}, {_procedimientoId}, UTC_TIMESTAMP(), 30);
                """);
        }

        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId, citaId: citaId));

        var citas = new CitaRepository(_factory);
        var cita = await citas.ObtenerPorIdAsync(citaId);
        cita!.FacturaId.Should().Be(r.FacturaId);
        cita.Estado.Should().Be(EstadoCita.Atendida, "si se cobró, el paciente vino");
    }

    // =========================================================
    // Reimpresión
    // =========================================================

    [Fact]
    public async Task Reimprimir_SaleIgualQueElPapelOriginal()
    {
        var original = await _ventas.RegistrarVentaAsync(
            Cobro([Consulta(), Gasa()], _medicoId, _arsId, cubierto: 1000m, ncf: "B0200000009"));

        var copia = FacturaService.AVentaResultado(
            (await _facturas.ObtenerCompletaAsync(original.FacturaId))!);

        copia.NumeroFactura.Should().Be(original.NumeroFactura);
        copia.Totales.Total.Should().Be(original.Totales.Total);
        copia.Totales.Itbis.Should().Be(original.Totales.Itbis);
        copia.Ncf.Should().Be(original.Ncf);
        copia.Honorario!.Monto.Should().Be(original.Honorario!.Monto);
        copia.Ars!.PacientePaga.Should().Be(original.Ars!.PacientePaga);
        copia.Lineas.Should().HaveCount(2);
        copia.Lineas.Count(l => l.EsProcedimiento).Should().Be(1);
    }

    // =========================================================
    // Rebaja de precio (pedido de la clínica 2026-08-27)
    // =========================================================

    private VentaLinea ConsultaRebajada(decimal cobrado, decimal lista = 1500m) =>
        new(null, "Consulta general", 1, cobrado, Exento: true,
            ProcedimientoId: _procedimientoId, PrecioCatalogo: lista);

    private VentaLinea GasaRebajada(decimal cobrado, decimal lista = 100m, int cantidad = 1) =>
        new(_insumoId, "Gasa estéril", cantidad, cobrado, Exento: false,
            ProcedimientoId: null, PrecioCatalogo: lista);

    [Fact]
    public async Task Rebaja_SeCobraElPrecioRebajado()
    {
        var r = await _ventas.RegistrarVentaAsync(
            Cobro([ConsultaRebajada(cobrado: 1200m)], _medicoId));

        r.Totales.Total.Should().Be(1200m);
        r.Totales.Descuento.Should().Be(300m);

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);
        leida!.Totales.Total.Should().Be(1200m, "la factura guarda lo que se cobró");
    }

    /// <summary>
    /// La rebaja tiene que SOBREVIVIR a la reimpresión. Sin `precio_catalogo`
    /// en el detalle, una consulta rebajada a 1,200 sería indistinguible de una
    /// que siempre valió 1,200, y la copia del comprobante saldría sin la línea
    /// de descuento que tenía el papel original.
    /// </summary>
    [Fact]
    public async Task Rebaja_LaReimpresionSigueMostrandoElDescuento()
    {
        var r = await _ventas.RegistrarVentaAsync(
            Cobro([ConsultaRebajada(cobrado: 1200m)], _medicoId));

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);
        var copia = FacturaService.AVentaResultado(leida!);

        copia.Totales.Descuento.Should().Be(300m);
        copia.Totales.SubtotalSinRebaja.Should().Be(1500m);
        copia.Totales.HuboRebaja.Should().BeTrue();
    }

    /// <summary>
    /// Y el precio de lista queda CONGELADO: subir el tarifario mañana no puede
    /// convertir una rebaja de 300 en una de 800 en un comprobante ya entregado.
    /// Es la misma regla que ya protegía al nombre y a la exención.
    /// </summary>
    [Fact]
    public async Task Rebaja_SubirElTarifarioNoAgrandaElDescuentoDeUnaFacturaVieja()
    {
        var r = await _ventas.RegistrarVentaAsync(
            Cobro([ConsultaRebajada(cobrado: 1200m)], _medicoId));

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion,
                $"UPDATE procedimiento SET precio = 2000.00 WHERE id = {_procedimientoId};");
        }

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);
        FacturaService.AVentaResultado(leida!).Totales.Descuento.Should().Be(300m);
    }

    [Fact]
    public async Task Rebaja_SinTocarElPrecio_NoSeGuardaNadaEnPrecioCatalogo()
    {
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);

        leida!.Lineas[0].PrecioCatalogo.Should().BeNull(
            "escribir el mismo número dos veces en cada línea no documenta nada");
        FacturaService.AVentaResultado(leida).Totales.HuboRebaja.Should().BeFalse();
    }

    /// <summary>
    /// El ITBIS del insumo sale del precio REBAJADO. Cobrarlo sobre el de lista
    /// le haría pagar al paciente 18% de una plata que nadie le cobró.
    /// </summary>
    [Fact]
    public async Task Rebaja_ElItbisPersistidoSaleDelPrecioQueSeCobro()
    {
        var r = await _ventas.RegistrarVentaAsync(
            Cobro([GasaRebajada(cobrado: 80m)], null));

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);

        leida!.Totales.Itbis.Should().Be(14.40m, "18% de 80, no de 100");
        leida.Totales.Total.Should().Be(94.40m);
    }

    /// <summary>
    /// El honorario congelado en la factura también sale de lo rebajado: si
    /// saliera del de lista, la clínica pagaría porcentaje sobre plata que no
    /// entró.
    /// </summary>
    [Fact]
    public async Task Rebaja_ElHonorarioGuardadoSaleDeLoQueSeCobro()
    {
        var r = await _ventas.RegistrarVentaAsync(
            Cobro([ConsultaRebajada(cobrado: 1000m)], _medicoId));

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);

        leida!.Honorario!.Monto.Should().Be(400m, "40% de 1,000 y no de 1,500");
    }

    /// <summary>
    /// La pantalla ya deja la columna de solo lectura sin el permiso, pero eso
    /// es comodidad. La regla vive en el servicio, que es por donde pasa TODO
    /// cobro (CLAUDE.md §8.8).
    /// </summary>
    [Fact]
    public async Task Rebaja_SinElPermiso_SeNiegaElCobro()
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "test", "Recepción", "Cajero",
            ["vender"], DateTime.UtcNow, 1);

        var accion = async () => await _ventas.RegistrarVentaAsync(
            Cobro([ConsultaRebajada(cobrado: 1200m)], _medicoId));

        await accion.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*permiso para rebajar precios*");
    }

    /// <summary>
    /// Y sin el permiso se puede seguir cobrando normal: la validación tiene
    /// que frenar la rebaja, no el mostrador.
    /// </summary>
    [Fact]
    public async Task Rebaja_SinElPermiso_ElCobroAPrecioDeListaSigueSaliendo()
    {
        SesionActual.Cerrar();
        SesionActual.Iniciar(_usuarioId, "test", "Recepción", "Cajero",
            ["vender"], DateTime.UtcNow, 1);

        var accion = async () => await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        await accion.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Reimprimir_ConservaElNombreQueTeniaAlCobrar()
    {
        var r = await _ventas.RegistrarVentaAsync(Cobro([Consulta()], _medicoId));

        await using (var conexion = new MySqlConnection(CadenaTest))
        {
            await conexion.OpenAsync();
            await Ejecutar(conexion,
                $"UPDATE procedimiento SET nombre = 'Consulta especializada' WHERE id = {_procedimientoId};");
        }

        var leida = await _facturas.ObtenerCompletaAsync(r.FacturaId);

        leida!.Lineas[0].NombreProducto.Should().Be("Consulta general",
            "el comprobante entregado no cambia porque se renombró el catálogo");
    }
}
