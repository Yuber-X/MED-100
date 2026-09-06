using MySqlConnector;
using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// El cerebro financiero del POS. Reglas innegociables (spec §9):
///  - ITBIS calculado SOBRE EL SUBTOTAL, nunca acumulando redondeos por línea.
///  - Redondeo Math.Round(v, 2, AwayFromZero); el modo (centavo/peso/arriba)
///    solo afecta el TOTAL. Matemática documentada en docs/ITBIS.md.
///  - Emisión atómica: número FOR UPDATE + factura + detalles + stock +
///    auditoría en UNA transacción; cualquier fallo revierte todo.
///  - El stock se valida al facturar (otro cajero pudo vender lo mismo).
/// Los métodos estáticos son puros y testeables sin base de datos.
/// </summary>
public class VentaService
{
    private readonly FacturaRepository _facturas;
    private readonly ClienteRepository _clientes;
    private readonly MedicoRepository _medicos;
    private readonly ArsRepository _ars;
    private readonly ConfiguracionNegocioService _config;
    private readonly AuditoriaService _auditoria;
    private readonly NcfService _ncf;

    public VentaService(FacturaRepository facturas, ClienteRepository clientes,
        MedicoRepository medicos, ArsRepository ars,
        ConfiguracionNegocioService config, AuditoriaService auditoria,
        NcfService ncf)
    {
        _facturas = facturas;
        _clientes = clientes;
        _medicos = medicos;
        _ars = ars;
        _config = config;
        _auditoria = auditoria;
        _ncf = ncf;
    }

    // ------------------------------------------------------------------
    // Cálculos puros
    // ------------------------------------------------------------------

    /// <summary>
    /// Totales del cobro. La cuenta vive en <see cref="CalculosClinica"/>: el
    /// ITBIS sale SOLO de las líneas gravadas, porque los servicios de salud
    /// están exentos y los insumos no.
    /// </summary>
    public static VentaTotales CalcularTotales(
        IReadOnlyList<VentaLinea> lineas, decimal itbisTasa, ModoRedondeo redondeo) =>
        CalculosClinica.CalcularTotales(lineas, itbisTasa, redondeo);

    /// <summary>Cambio = efectivo − total. Solo aplica a pagos en efectivo.</summary>
    public static decimal CalcularCambio(decimal efectivoRecibido, decimal total) =>
        efectivoRecibido - total;

    /// <summary>F-0001 (simple) o F-2026-0001 (con año). El año es el del día de negocio.</summary>
    public static string FormatearNumeroFactura(
        string prefijo, long numero, FormatoFactura formato, int anio) => formato switch
    {
        FormatoFactura.ConAnio => $"{prefijo}{anio}-{numero:0000}",
        _ => $"{prefijo}{numero:0000}"
    };

    // ------------------------------------------------------------------
    // Emisión
    // ------------------------------------------------------------------

    public async Task<VentaResultado> RegistrarVentaAsync(VentaSolicitud solicitud, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("vender"))
            throw new InvalidOperationException("No tienes permiso para vender.");

        Validar(solicitud);

        var cfg = _config.Actual;
        // Tasa EFECTIVA: 0 si el ITBIS está desactivado en Configuración
        // (la factura persiste itbis_tasa = 0 e itbis = 0, coherente con el ticket)
        var totales = CalcularTotales(solicitud.Lineas, cfg.ItbisTasaEfectiva, cfg.Redondeo);

        // El honorario y el reparto con la ARS se resuelven ANTES de abrir la
        // transacción: son lecturas de catálogo, y tenerlas dentro alargaría
        // el bloqueo del número de factura sin ninguna ganancia.
        var honorario = await ResolverHonorarioAsync(solicitud, ct);
        var ars = await ResolverArsAsync(solicitud, totales.Total, cfg, ct);

        // El efectivo se compara contra lo que paga el PACIENTE, no contra el
        // total: si la ARS cubre 1200 de 1500, el paciente pone 300 y pedirle
        // los 1500 sería un error de cobro en el mostrador.
        var aCobrar = ars.PacientePaga;

        // ---- Fiado (012) -----------------------------------------------
        // Lo que de verdad entra AHORA. Si no se indicó nada, entra todo, que
        // es el caso normal y deja el comportamiento anterior intacto.
        var abonado = ValidarFiado(solicitud, aCobrar);

        decimal? efectivo = null, cambio = null;
        if (solicitud.MetodoPago is MetodoPagoFactura.Efectivo)
        {
            if (solicitud.EfectivoRecibido is not { } recibido)
                throw new ArgumentException("Indica el efectivo recibido.");
            // Se compara contra lo que se está ABONANDO, no contra la deuda
            // entera: en un fiado el paciente entrega menos a propósito, y
            // exigirle el total acá haría imposible fiar en efectivo.
            if (recibido < abonado)
                throw new ArgumentException(
                    $"El efectivo recibido no cubre lo que el paciente está pagando ({abonado:0.00}).");
            efectivo = recibido;
            cambio = CalcularCambio(recibido, abonado);
        }
        else if (solicitud.MetodoPago is MetodoPagoFactura.Mixto)
        {
            // Mixto: la parte en efectivo es informativa; el cambio se maneja manual
            efectivo = solicitud.EfectivoRecibido;
        }

        var ahoraUtc = DateTime.UtcNow;

        // ---- Comprobante fiscal (011) ----------------------------------
        // Manda lo que escribió el cajero. Si dejó la caja vacía, se toma el
        // siguiente de la secuencia autorizada, pero SOLO si hay una cargada:
        // cuando no la hay, la factura sale sin NCF igual que siempre, que es
        // un recibo interno perfectamente válido.
        var ncfManual = string.IsNullOrWhiteSpace(solicitud.Ncf)
            ? null
            : solicitud.Ncf.Trim().ToUpperInvariant();
        var haySecuencia = ncfManual is null && await _ncf.ObtenerSecuenciaAsync(ct) is { Activo: true };

        using var conexion = await _facturas.AbrirConexionAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            // Dentro de la transacción y con FOR UPDATE: dos cajeros cobrando a
            // la vez no pueden recibir el mismo comprobante, y si la venta falla
            // más abajo el número no se consume. Un NCF quemado no se recupera.
            var ncfFinal = haySecuencia
                ? await NcfRepository.ReservarSiguienteAsync(conexion, transaccion, FechaNegocio.Hoy, ct)
                : ncfManual;

            var numero = await ConfiguracionNegocioRepository.ReservarNumeroFacturaAsync(conexion, transaccion, ct);
            var numeroFactura = FormatearNumeroFactura(
                cfg.FacturaPrefijo, numero, cfg.FacturaFormato, FechaNegocio.Hoy.Year);

            // Stock: SOLO los insumos. Un procedimiento no sale de ningún
            // estante — descontarle inventario no significa nada.
            foreach (var linea in solicitud.Lineas.Where(l => l.ProductoId is not null))
            {
                if (!await FacturaRepository.DescontarStockAsync(
                        conexion, transaccion, linea.ProductoId!.Value, linea.Cantidad, ct))
                    throw new InvalidOperationException(
                        $"Stock insuficiente de \"{linea.NombreProducto}\". " +
                        "Otro cajero pudo haberlo vendido; actualiza el carrito.");
            }

            var facturaId = await FacturaRepository.InsertarFacturaAsync(
                conexion, transaccion, numeroFactura, solicitud.ClienteId, SesionActual.Id,
                ahoraUtc, totales, solicitud.MetodoPago, efectivo, cambio,
                honorario, ars, ncfFinal, abonado, solicitud.FechaCompromiso, ct);

            foreach (var linea in solicitud.Lineas)
                await FacturaRepository.InsertarDetalleAsync(conexion, transaccion, facturaId, linea, ct);

            // La cita queda unida a su factura: es lo que después permite decir
            // cuáles de las citas del día se cobraron y cuáles no.
            if (solicitud.CitaId is { } citaId)
                await FacturaRepository.EnlazarCitaAsync(conexion, transaccion, citaId, facturaId, ct);

            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Crear, DbNames.Factura, facturaId,
                Describir(numeroFactura, solicitud, totales, honorario, ars, ncfFinal),
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);

            // DESPUÉS del commit y sin propagar: si el cajero pegó un NCF a mano,
            // la secuencia se corre para seguir a partir de ese. Que eso falle no
            // puede mostrarle un error por un cobro que ya se guardó bien.
            if (ncfManual is not null)
                await _ncf.AdoptarComoPredeterminadaAsync(ncfManual, ct);

            string? nombreCliente = null;
            if (solicitud.ClienteId is { } clienteId)
                nombreCliente = (await _clientes.ObtenerPorIdAsync(clienteId, ct))?.Nombre;

            return new VentaResultado(facturaId, numeroFactura, ahoraUtc, totales,
                efectivo, cambio, solicitud.Lineas, nombreCliente, solicitud.MetodoPago,
                honorario, ars, ncfFinal, solicitud.ClienteId,
                abonado, solicitud.FechaCompromiso);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Lee el porcentaje VIGENTE del médico y calcula su parte. De acá sale ya
    /// congelado para copiarse a la factura: cambiarle el porcentaje mañana no
    /// puede reescribir lo que se pagó hoy.
    /// </summary>
    private async Task<HonorarioMedico?> ResolverHonorarioAsync(
        VentaSolicitud solicitud, CancellationToken ct)
    {
        if (solicitud.MedicoId is not { } medicoId)
            return null;

        var medico = await _medicos.ObtenerPorIdAsync(medicoId, ct)
            ?? throw new ArgumentException("El médico no existe o fue eliminado.");
        if (!medico.Activo)
            throw new ArgumentException($"{medico.Nombre} está inactivo y no se le puede facturar.");

        return CalculosClinica.CalcularHonorario(
            solicitud.Lineas, medico.Id, medico.Nombre, medico.PorcentajeHonorario);
    }

    private async Task<RepartoArs> ResolverArsAsync(VentaSolicitud solicitud, decimal total,
        ConfiguracionNegocio cfg, CancellationToken ct)
    {
        if (solicitud.ArsId is not { } arsId)
            return CalculosClinica.CalcularReparto(total, null, null, null, 0m);

        if (!cfg.ArsActivo)
            throw new ArgumentException(
                "El módulo de seguros está apagado en Configuración. Encendelo para facturar con ARS.");

        var ars = await _ars.ObtenerPorIdAsync(arsId, ct)
            ?? throw new ArgumentException("La ARS no existe.");
        if (!ars.Activo)
            throw new ArgumentException($"{ars.Nombre} está inactiva y no se le puede facturar.");

        return CalculosClinica.CalcularReparto(total, ars.Id, ars.Nombre,
            solicitud.ArsAutorizacion, solicitud.ArsCubierto);
    }

    /// <summary>
    /// Comprueba el fiado y devuelve lo que entra a la caja AHORA.
    ///
    /// Sin <c>AbonadoInicial</c> entra todo: es el camino de siempre y no pide
    /// permiso ni fecha, porque no hay deuda que administrar.
    /// </summary>
    private static decimal ValidarFiado(VentaSolicitud solicitud, decimal aCobrar)
    {
        if (solicitud.AbonadoInicial is not { } abonado)
            return aCobrar;

        if (abonado < 0m)
            throw new ArgumentException("Lo que entrega el paciente no puede ser negativo.");

        if (abonado > aCobrar)
            throw new ArgumentException(
                $"El paciente no puede abonar más de lo que debe ({aCobrar:0.00}). " +
                "Un pago por adelantado contra consultas futuras no se registra acá.");

        // Pagó todo: no es un fiado aunque se haya llegado por este camino.
        if (abonado == aCobrar)
            return abonado;

        if (!SesionActual.TienePermiso("fiados"))
            throw new InvalidOperationException(
                "No tienes permiso para dejar una factura con saldo pendiente. " +
                "Pedile a un administrador que la cobre o que te dé el permiso.");

        // La fecha es OBLIGATORIA y no es burocracia: es de donde sale el
        // semáforo y el aviso automático. Una deuda sin fecha no le aparece a
        // nadie nunca, y el fiado se descubre meses después revisando papeles.
        if (solicitud.FechaCompromiso is not { } compromiso)
            throw new ArgumentException(
                "Indicá para cuándo se compromete a pagar. Sin fecha, la deuda no " +
                "aparece en los avisos y se pierde de vista.");

        if (compromiso < FechaNegocio.Hoy)
            throw new ArgumentException(
                "La fecha de pago no puede ser anterior a hoy: nacería atrasada.");

        return abonado;
    }

    private static string Describir(string numeroFactura, VentaSolicitud solicitud,
        VentaTotales totales, HonorarioMedico? honorario, RepartoArs ars, string? ncf)
    {
        var texto = $"Factura {numeroFactura} emitida: {solicitud.Lineas.Count} líneas, " +
                    $"total {totales.Total:0.00}";
        if (honorario is { Monto: > 0m })
            texto += $" · honorario {honorario.MedicoNombre} {honorario.Porcentaje:0.##}% = {honorario.Monto:0.00}";
        if (ars.ArsId is not null)
            texto += $" · {ars.ArsNombre} cubre {ars.Cubierto:0.00}, paciente {ars.PacientePaga:0.00}";
        if (!string.IsNullOrWhiteSpace(ncf))
            texto += $" · NCF {ncf}";
        return texto;
    }

    private static void Validar(VentaSolicitud solicitud)
    {
        if (solicitud.Lineas.Count == 0)
            throw new ArgumentException("El carrito está vacío.");
        if (solicitud.Lineas.Any(l => l.Cantidad <= 0))
            throw new ArgumentException("Las cantidades deben ser mayores que cero.");
        if (solicitud.Lineas.Any(l => l.PrecioUnitario < 0m))
            throw new ArgumentException("Hay precios inválidos en el carrito.");

        // Rebajar un precio necesita su propio permiso. La pantalla ya deja la
        // columna de solo lectura sin él, pero eso es comodidad, no seguridad:
        // la regla vive acá, que es por donde pasa TODO cobro (CLAUDE.md §8.8).
        if (solicitud.Lineas.Any(l => l.Descuento > 0m) &&
            !SesionActual.TienePermiso("precio_editar"))
        {
            throw new ArgumentException(
                "No tienes permiso para rebajar precios. Pedí que se cobre al precio de lista, " +
                "o que un supervisor autorice la rebaja.");
        }

        // Cada línea es un procedimiento O un insumo, nunca las dos ni ninguna
        // (lo mismo que exige ck_detalle_una_cosa; se atrapa acá para dar un
        // mensaje entendible en vez de un error de restricción de MySQL).
        if (solicitud.Lineas.Any(l => l.ProductoId is null && l.ProcedimientoId is null))
            throw new ArgumentException("Hay una línea que no es ni procedimiento ni insumo.");
        if (solicitud.Lineas.Any(l => l.ProductoId is not null && l.ProcedimientoId is not null))
            throw new ArgumentException("Una línea no puede ser procedimiento e insumo a la vez.");

        if (solicitud.Lineas.Where(l => l.ProductoId is not null)
                .GroupBy(l => l.ProductoId).Any(g => g.Count() > 1))
            throw new ArgumentException("Hay insumos repetidos en el carrito; combina las líneas.");

        // Un servicio de salud lo presta alguien: sin médico no hay de dónde
        // salga el honorario ni qué imprimir en la factura del paciente.
        if (solicitud.Lineas.Any(l => l.EsProcedimiento) && solicitud.MedicoId is null)
            throw new ArgumentException("Elegí el médico: la factura tiene procedimientos.");

        if (solicitud.ArsCubierto > 0m && solicitud.ArsId is null)
            throw new ArgumentException("Elegí la ARS antes de indicar lo que cubre.");
    }
}
