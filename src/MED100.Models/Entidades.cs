namespace MED100.Models;

/// <summary>Empleado del negocio (sistema multiusuario con roles).</summary>
public class Usuario
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Apellido { get; set; }
    public int? RolId { get; set; }
    public string? RolNombre { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();
}

/// <summary>Rol del catálogo (Admin / Supervisor / Cajero / Servicio).</summary>
public class Rol
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
}

/// <summary>Permiso del catálogo (código estable usado por la UI y los services).</summary>
public class Permiso
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
}

/// <summary>Registro de login/logout en la tabla sesion (alimenta el cuadre).</summary>
public class Sesion
{
    public long Id { get; set; }
    public long UsuarioId { get; set; }
    public DateTime LoginAtUtc { get; set; }
    public DateTime? LogoutAtUtc { get; set; }
    public string? IpLocal { get; set; }
}

/// <summary>
/// El PACIENTE. La tabla sigue llamándose <c>cliente</c> porque así la llama
/// el dueño y así vino del POS-500; la UI dice "Paciente".
///
/// Ojo con lo que NO va acá: diagnósticos, tratamientos, resultados ni notas
/// médicas. MED-100 es la recepción, no el expediente clínico (CLAUDE.md §1.1).
/// <c>Notas</c> es administrativo: "prefiere turno en la tarde", "viene con su mamá".
/// </summary>
public class Cliente
{
    public long Id { get; set; }
    public string? Cedula { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    /// <summary>Por acá sale el recordatorio de cita. Es la razón de que exista la columna.</summary>
    public string? Email { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
    public SexoPaciente? Sexo { get; set; }
    public string? Direccion { get; set; }
    /// <summary>Quién lo refirió. NULL = llegó por su cuenta.</summary>
    public long? ReferidorId { get; set; }
    /// <summary>Nombre del referidor, resuelto por JOIN. No se persiste desde acá.</summary>
    public string? ReferidorNombre { get; set; }
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>
/// Datos editables de un paciente (formulario nuevo/editar). Los campos de
/// clínica van al final con default para no romper a quien ya lo construye
/// con los cinco originales.
/// </summary>
public record ClienteDatos(
    string? Cedula,
    string Nombre,
    string? Telefono,
    string? Direccion,
    string? Notas,
    string? Email = null,
    DateOnly? FechaNacimiento = null,
    SexoPaciente? Sexo = null,
    long? ReferidorId = null);

/// <summary>
/// Origen del paciente ("registro de proveniento"): el médico que lo mandó,
/// la ARS, una campaña, otro paciente. Catálogo chico, sin soft delete: se
/// desactiva con <c>Activo</c> y la FK del paciente es ON DELETE SET NULL.
/// </summary>
public class Referidor
{
    public long Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public TipoReferidor Tipo { get; set; } = TipoReferidor.Otro;
    public string? Notas { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Datos editables de un referidor.</summary>
public record ReferidorDatos(string Nombre, TipoReferidor Tipo, string? Notas = null, bool Activo = true);

/// <summary>
/// Producto del almacén. La UI lo llama "insumo" porque es la mayoría, pero
/// <see cref="Tipo"/> distingue las familias que de verdad hay en una clínica.
/// Código (barras) opcional. Soft delete.
/// </summary>
public class Producto
{
    public long Id { get; set; }
    public string? Codigo { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public int Cantidad { get; set; }
    /// <summary>Insumo, medicamento, material, equipo… Por defecto insumo.</summary>
    public TipoProducto Tipo { get; set; } = TipoProducto.Insumo;
    public string? Descripcion { get; set; }
    public DateOnly? FechaCaducidad { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Datos editables de un producto (formulario nuevo/editar).</summary>
public record ProductoDatos(string? Codigo, string Nombre, decimal Precio, int Cantidad,
    string? Descripcion, DateOnly? FechaCaducidad,
    TipoProducto Tipo = TipoProducto.Insumo);

/// <summary>Totales del módulo Almacén (calculados en SQL, no en UI).</summary>
public record AlmacenTotales(int TotalProductos, long TotalUnidades, decimal ValorInventario);

/// <summary>
/// Configuración compartida del negocio (tabla configuracion_negocio, fila única).
/// Se carga al iniciar y se expone vía ConfiguracionNegocioService.
/// </summary>
public class ConfiguracionNegocio
{
    public string NombreNegocio { get; set; } = "Mi Negocio";
    public string? Rnc { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? LogoRuta { get; set; }
    public bool ItbisActivo { get; set; } = true;   // OFF: sin ITBIS en venta ni ticket
    public decimal ItbisTasa { get; set; } = 18.00m;
    /// <summary>Tasa que realmente se aplica al vender (0 si el ITBIS está apagado).</summary>
    public decimal ItbisTasaEfectiva => ItbisActivo ? ItbisTasa : 0m;
    public ModoRedondeo Redondeo { get; set; } = ModoRedondeo.Centavo;
    public string MonedaSimbolo { get; set; } = "RD$";
    public string FormatoMiles { get; set; } = "coma";
    public string FacturaPrefijo { get; set; } = "F-";
    public long FacturaSiguiente { get; set; } = 1;
    public FormatoFactura FacturaFormato { get; set; } = FormatoFactura.Simple;
    public bool MostrarClienteEnVenta { get; set; } = true;   // regla Yuber 2026-07-11
    /// <summary>
    /// Módulo de seguros. OFF = la pantalla de cobro ni menciona la ARS
    /// (pedido 2026-08-10: "un checkbox para deshabilitarlo si no llegan a usarlo").
    /// </summary>
    public bool ArsActivo { get; set; } = true;
    /// <summary>Prefijo del número de turno de la sala ("A" → A-15). Puede quedar vacío.</summary>
    public string TurnoPrefijo { get; set; } = string.Empty;
    /// <summary>Imprimir el papelito del turno sin preguntar. Es el flujo normal en recepción.</summary>
    public bool TurnoImprimirAuto { get; set; } = true;

    /// <summary>
    /// Guardar una copia en PDF de cada factura en el expediente del paciente
    /// (pedido 2026-08-15). ON por defecto: es un comprobante fiscal, y tenerlo
    /// a mano evita reimprimir de memoria cuando el paciente vuelve a pedirlo.
    /// Las facturas de consumidor final no se archivan — no hay expediente
    /// donde ponerlas.
    /// </summary>
    public bool ArchivarFacturaPdf { get; set; } = true;

    /// <summary>
    /// Ídem para el papelito del turno. OFF por defecto: es un papel que se
    /// tira al salir del consultorio, y un paciente frecuente acumularía
    /// cuarenta PDF al año que no le sirven a nadie.
    /// </summary>
    public bool ArchivarTurnoPdf { get; set; }
}

/// <summary>
/// Línea del cobro: un PROCEDIMIENTO o un INSUMO, nunca las dos ni ninguna
/// (lo mismo que exige ck_detalle_una_cosa en la base).
///
/// <c>Exento</c> viaja en la línea y no se deduce del tipo: los servicios de
/// salud están exentos de ITBIS y los insumos no, pero la decisión final es
/// del contador y puede cambiar por producto. Deducirlo acá haría que
/// cambiarlo en el catálogo reescribiera facturas viejas.
/// </summary>
public record VentaLinea(long? ProductoId, string NombreProducto, int Cantidad,
    decimal PrecioUnitario, bool Exento = false, long? ProcedimientoId = null)
{
    public decimal Subtotal => Cantidad * PrecioUnitario;

    public bool EsProcedimiento => ProcedimientoId is not null;

    /// <summary>Línea de procedimiento: exenta por defecto y sin descuento de stock.</summary>
    public static VentaLinea DeProcedimiento(long procedimientoId, string nombre, int cantidad,
        decimal precio, bool exento = true) =>
        new(null, nombre, cantidad, precio, exento, procedimientoId);

    /// <summary>Línea de insumo: descuenta inventario y normalmente lleva ITBIS.</summary>
    public static VentaLinea DeInsumo(long productoId, string nombre, int cantidad,
        decimal precio, bool exento = false) =>
        new(productoId, nombre, cantidad, precio, exento);
}

/// <summary>
/// Solicitud de cobro (lo que la recepción confirmó en pantalla).
///
/// <c>MedicoId</c> es opcional pero se exige si hay procedimientos: un servicio
/// de salud lo presta alguien, y de ahí sale el reparto de honorarios.
/// </summary>
public record VentaSolicitud(
    IReadOnlyList<VentaLinea> Lineas,
    long? ClienteId,                       // NULL = consumidor final (regla Yuber)
    MetodoPagoFactura MetodoPago,
    decimal? EfectivoRecibido,
    long? MedicoId = null,
    long? ArsId = null,
    string? ArsAutorizacion = null,
    /// <summary>Lo que cubre el seguro. NO es un descuento: sale de otra caja.</summary>
    decimal ArsCubierto = 0m,
    /// <summary>Comprobante fiscal asignado a mano. NULL = sin NCF.</summary>
    string? Ncf = null,
    /// <summary>Cita que se está cobrando, si el cobro salió de la agenda.</summary>
    long? CitaId = null);

/// <summary>
/// Totales calculados del cobro.
///
/// <c>BaseGravada</c> es la parte del subtotal que SÍ paga ITBIS: en una
/// clínica casi todo es servicio de salud exento, y el ITBIS sale solo de los
/// insumos. Se expone porque es el número que hay que poder explicarle al
/// contador cuando pregunte de dónde salió el impuesto.
/// </summary>
public record VentaTotales(decimal Subtotal, decimal ItbisTasa, decimal Itbis, decimal Total,
    decimal BaseGravada = 0m)
{
    public decimal BaseExenta => Subtotal - BaseGravada;
}

/// <summary>
/// Reparto del honorario del médico, calculado al emitir.
///
/// La base son SOLO los procedimientos: al médico no le toca porcentaje de la
/// gasa ni del suero. El porcentaje se copia a la factura y no se vuelve a
/// leer del catálogo (CLAUDE.md §1.3.2).
/// </summary>
public record HonorarioMedico(long? MedicoId, string? MedicoNombre,
    decimal Porcentaje, decimal Base, decimal Monto);

/// <summary>
/// Reparto del cobro entre el seguro y el paciente.
/// <c>Cubierto + PacientePaga = Total</c>. Lo que cubre la ARS no entra en la
/// caja del día: por eso van separados y no como descuento (CLAUDE.md §1.3.3).
/// </summary>
public record RepartoArs(long? ArsId, string? ArsNombre, string? Autorizacion,
    decimal Cubierto, decimal PacientePaga);

/// <summary>Resultado de un cobro registrado (para el ticket y la reimpresión).</summary>
public record VentaResultado(
    long FacturaId,
    string NumeroFactura,
    DateTime FechaEmisionUtc,
    VentaTotales Totales,
    decimal? EfectivoRecibido,
    decimal? Cambio,
    IReadOnlyList<VentaLinea> Lineas,
    string? NombreCliente,
    MetodoPagoFactura MetodoPago,
    HonorarioMedico? Honorario = null,
    RepartoArs? Ars = null,
    string? Ncf = null,
    /// <summary>
    /// El paciente, o NULL si fue consumidor final. Es lo que decide a qué
    /// expediente va la copia en PDF del comprobante.
    /// </summary>
    long? ClienteId = null);

/// <summary>Aseguradora. Catálogo chico, se desactiva en vez de borrarse.</summary>
public class Ars
{
    public long Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Rnc { get; set; }
    public string? Telefono { get; set; }
    public string? Notas { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Datos editables de una ARS.</summary>
public record ArsDatos(string Nombre, string? Rnc = null, string? Telefono = null,
    string? Notas = null, bool Activo = true);

/// <summary>Fila de la lista de comprobantes (sin sus líneas).</summary>
public record FacturaResumen(
    long Id,
    string NumeroFactura,
    DateTime FechaEmisionUtc,
    string? NombreCliente,
    string NombreCajero,
    long UsuarioId,
    decimal Total,
    MetodoPagoFactura MetodoPago,
    EstadoFactura Estado,
    string? AnuladaMotivo,
    /// <summary>
    /// El paciente, o NULL si fue consumidor final. Hace falta para saber a qué
    /// expediente va la copia en PDF del comprobante: sin id no hay expediente.
    /// </summary>
    long? ClienteId = null);

/// <summary>
/// Línea de una factura ya emitida (leída de BD). El nombre es el que se
/// guardó al cobrar, no el actual del catálogo: un comprobante entregado no
/// cambia porque alguien renombró el procedimiento después.
/// </summary>
public record FacturaLinea(long? ProductoId, string NombreProducto, int Cantidad,
    decimal PrecioUnitario, decimal Subtotal,
    long? ProcedimientoId = null, bool Exento = false)
{
    public bool EsProcedimiento => ProcedimientoId is not null;
}

/// <summary>Factura completa para ver el detalle y reimprimir el ticket.</summary>
public record FacturaCompleta(FacturaResumen Resumen, VentaTotales Totales,
    decimal? EfectivoRecibido, decimal? Cambio, IReadOnlyList<FacturaLinea> Lineas,
    HonorarioMedico? Honorario = null, RepartoArs? Ars = null, string? Ncf = null);

/// <summary>Filtros de la búsqueda de comprobantes.</summary>
public record FiltroComprobantes(
    string? Texto,          // número de factura o nombre de cliente
    DateOnly? Desde,        // día de negocio (UTC-4)
    DateOnly? Hasta,
    long? UsuarioId,        // null = todos (requiere permiso comprobantes_todos)
    int Limite = 200);

/// <summary>Totales del cuadre de un cajero en un día de negocio.</summary>
public record CuadreResumen(
    long UsuarioId,
    string NombreCajero,
    DateOnly Fecha,
    int TotalFacturas,
    decimal TotalVendido,
    decimal TotalEfectivo,
    decimal TotalTarjeta,
    decimal TotalTransferencia,
    decimal TotalMixto,
    int FacturasAnuladas,
    decimal MontoAnulado,
    int TiempoActivoSegundos,
    bool YaCerrado)
{
    public string TiempoActivoTexto
    {
        get
        {
            var t = TimeSpan.FromSeconds(TiempoActivoSegundos);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes}min"
                : $"{t.Minutes}min";
        }
    }
}

// ---------------------------------------------------------------------
// Analítica (Fase 5)
// ---------------------------------------------------------------------

/// <summary>Ventas de un día de negocio (para el gráfico de tendencia).</summary>
public record VentaDiaria(DateOnly Fecha, decimal Monto, int Facturas);

/// <summary>Ranking de cajeros/vendedores por ventas.</summary>
public record VendedorRanking(string Nombre, int Facturas, decimal Total);

/// <summary>Ranking de productos más vendidos.</summary>
public record ProductoRanking(string Nombre, int Unidades, decimal Total);

/// <summary>Totales por método de pago (reutilizado en Dashboard y Reportes).</summary>
public record TotalesPorMetodo(decimal Efectivo, decimal Tarjeta, decimal Transferencia, decimal Mixto);

/// <summary>Datos del Panel: KPIs del día/mes, tendencia y rankings.</summary>
public record DashboardDatos(
    decimal VentasHoy,
    int FacturasHoy,
    decimal VentasMes,
    decimal VentasMesAnterior,
    decimal TicketPromedioMes,
    int ProductosPorCaducar,
    int ProductosStockBajo,
    IReadOnlyList<VentaDiaria> VentasPorDia,
    IReadOnlyList<VendedorRanking> TopVendedores,
    IReadOnlyList<ProductoRanking> TopProductos);

/// <summary>Reporte de ventas de un rango de días de negocio.</summary>
public record ReporteVentas(
    DateOnly Desde,
    DateOnly Hasta,
    decimal TotalVendido,
    int TotalFacturas,
    decimal TotalItbis,
    decimal TicketPromedio,
    TotalesPorMetodo PorMetodo,
    int FacturasAnuladas,
    decimal MontoAnulado,
    IReadOnlyList<VentaDiaria> VentasPorDia,
    IReadOnlyList<ProductoRanking> TopProductos,
    IReadOnlyList<VendedorRanking> PorCajero);

/// <summary>
/// Cuadre GENERAL del día: el desglose de cada cajero + los totales del negocio
/// (pedido Yuber 2026-07-12). Es la vista por defecto del módulo.
/// </summary>
public record CuadreGeneral(
    DateOnly Fecha,
    IReadOnlyList<CuadreResumen> PorCajero,
    int TotalFacturas,
    decimal TotalVendido,
    decimal TotalEfectivo,
    decimal TotalTarjeta,
    decimal TotalTransferencia,
    decimal TotalMixto,
    int FacturasAnuladas,
    decimal MontoAnulado)
{
    public bool TodosCerrados => PorCajero.Count > 0 && PorCajero.All(c => c.YaCerrado);
    public bool HayPendientes => PorCajero.Any(c => !c.YaCerrado);
}

/// <summary>Tamaño del papel para imprimir el cierre de caja.</summary>
public enum TamanoImpresion
{
    Ticket80mm,
    Carta
}

/// <summary>Filtros del visor de Historial (auditoría).</summary>
public record FiltroAuditoria(
    DateOnly? Desde,
    DateOnly? Hasta,
    string? Entidad,
    AccionAuditoria? Accion,
    int Limite = 300);

/// <summary>Entrada del log de auditoría (inmutable, nunca se borra).</summary>
public class Auditoria
{
    public long Id { get; set; }
    public long UsuarioId { get; set; }
    public string Entidad { get; set; } = string.Empty;
    public long? EntidadId { get; set; }
    public AccionAuditoria Accion { get; set; }
    public string? Descripcion { get; set; }
    public string? IpLocal { get; set; }
    public DateTime TimestampUtc { get; set; }
}

// =============================================================
// CLÍNICA (MED-100) — Médicos y sus horarios de atención
// =============================================================

/// <summary>
/// Médico que atiende en la clínica. Información SIMPLE a propósito:
/// MED-100 es la recepción, no un registro profesional (ver §1.1 del CLAUDE.md).
/// </summary>
public class Medico
{
    public long Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Cedula { get; set; }
    /// <summary>Número que habilita a ejercer en RD. Va impreso en la factura.</summary>
    public string? Exequatur { get; set; }
    public string? Especialidad { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    /// <summary>
    /// Porcentaje VIGENTE de lo facturado que le corresponde. El que se cobró
    /// en cada factura se copia a la factura: cambiar esto no reescribe el pasado.
    /// </summary>
    public decimal PorcentajeHonorario { get; set; }
    /// <summary>
    /// Prefijo del turno de la sala: dos letras sacadas del nombre
    /// ("Yuber Santana Lizardo" → "YO"), así los números de ese médico se ven
    /// como YO-1, YO-2. NULL = usa el prefijo general del negocio.
    /// </summary>
    public string? CodigoTurno { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Datos editables de un médico (formulario nuevo/editar).</summary>
public record MedicoDatos(
    string Nombre,
    string? Cedula,
    string? Exequatur,
    string? Especialidad,
    string? Telefono,
    string? Email,
    decimal PorcentajeHonorario,
    bool Activo = true,
    /// <summary>Vacío = lo calcula el servicio a partir del nombre.</summary>
    string? CodigoTurno = null);

/// <summary>
/// Un tramo de atención de un médico. Mañana y tarde son DOS tramos del
/// mismo día: así se representa el corte del almuerzo.
///
/// <see cref="DiaSemana"/> sigue la convención de MySQL DAYOFWEEK():
/// 1 = domingo … 7 = sábado.
/// </summary>
public class MedicoHorario
{
    public long Id { get; set; }
    public long MedicoId { get; set; }
    public int DiaSemana { get; set; }
    public TimeOnly HoraInicio { get; set; }
    public TimeOnly HoraFin { get; set; }

    /// <summary>Largo del tramo en minutos. El fin es exclusivo (8 a 12 = 240).</summary>
    public int DuracionMinutos => (int)(HoraFin - HoraInicio).TotalMinutes;
}

/// <summary>Tramo horario tal como lo captura el formulario.</summary>
public record HorarioDatos(int DiaSemana, TimeOnly HoraInicio, TimeOnly HoraFin);

/// <summary>
/// Médico con su estado de atención AHORA, para la pantalla de recepción:
/// es lo que contesta "¿quién está disponible en este momento?".
/// </summary>
public record MedicoDisponibilidad(
    Medico Medico,
    bool AtiendeAhora,
    /// <summary>Tramo que está cubriendo ahora, o el próximo de hoy. Null = no atiende más hoy.</summary>
    MedicoHorario? Tramo,
    /// <summary>Texto listo para mostrar: "Atiende hasta las 12:00 p. m." / "Entra a las 2:00 p. m.".</summary>
    string Detalle,
    /// <summary>
    /// Días fijos en que atiende, con la convención DAYOFWEEK() (1 = domingo).
    /// Es la respuesta a "¿qué días viene la doctora?", que no es lo mismo que
    /// "¿está ahora?" y se pregunta mucho más seguido en el mostrador.
    /// </summary>
    IReadOnlyList<int>? Dias = null)
{
    public IReadOnlyList<int> DiasQueAtiende => Dias ?? [];
}

// =============================================================
// CLÍNICA (MED-100) — Tarifario de procedimientos
// =============================================================

/// <summary>
/// Un procedimiento del tarifario ("costo de procedimientos").
///
/// <see cref="ExentoItbis"/> viene en true por defecto: los servicios de salud
/// están exentos de ITBIS en RD. Se deja por procedimiento y no como una
/// bandera global porque la clínica también vende insumos, que sí pueden ir
/// gravados.
/// </summary>
public class Procedimiento
{
    public long Id { get; set; }
    public string? Codigo { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    /// <summary>Cuánto ocupa en la agenda del médico.</summary>
    public int DuracionMinutos { get; set; } = 30;
    public bool ExentoItbis { get; set; } = true;
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

/// <summary>Datos editables de un procedimiento (formulario nuevo/editar).</summary>
public record ProcedimientoDatos(
    string? Codigo,
    string Nombre,
    decimal Precio,
    int DuracionMinutos,
    bool ExentoItbis,
    string? Descripcion,
    bool Activo = true);

/// <summary>
/// Una cita de la agenda.
///
/// <b>FechaHoraUtc es UTC</b>, como todo en la base. La UI SIEMPRE la muestra
/// convertida a hora local de RD: una cita a las 3:00 de la tarde en Santo
/// Domingo se guarda como 19:00 UTC. Mostrar el valor crudo diría "7:00 pm"
/// y mandaría a los pacientes cuatro horas tarde.
///
/// Los nombres del paciente, del médico y del procedimiento vienen por JOIN:
/// son para mostrar, no se persisten acá. Ojo con la diferencia respecto a la
/// factura, donde los datos SÍ se copian — una factura entregada no puede
/// cambiar, pero una cita muestra siempre el nombre actual.
/// </summary>
public class Cita
{
    public long Id { get; set; }
    public long ClienteId { get; set; }
    public string PacienteNombre { get; set; } = string.Empty;
    public string? PacienteTelefono { get; set; }
    public string? PacienteEmail { get; set; }
    public long MedicoId { get; set; }
    public string MedicoNombre { get; set; } = string.Empty;
    /// <summary>A qué viene. NULL = consulta general.</summary>
    public long? ProcedimientoId { get; set; }
    public string? ProcedimientoNombre { get; set; }
    public DateTime FechaHoraUtc { get; set; }
    public int DuracionMinutos { get; set; } = 30;
    public EstadoCita Estado { get; set; } = EstadoCita.Programada;
    public string? Notas { get; set; }
    /// <summary>Se llena cuando el correo SALIÓ BIEN, nunca antes.</summary>
    public DateTime? RecordatorioEnviadoAtUtc { get; set; }
    /// <summary>Se llena al cobrarla. Es lo que une la agenda con la caja.</summary>
    public long? FacturaId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // La conversión a hora local NO vive acá: MED100.Models no conoce a
    // MED100.Common (donde está FechaNegocio) y no se le agrega la referencia
    // por una comodidad. La hacen los Services y los ViewModels.

    /// <summary>
    /// Una cita cancelada o marcada como "no asistió" LIBERA el hueco: se puede
    /// agendar a otro paciente en esa misma hora.
    /// </summary>
    public bool OcupaAgenda => Estado is not (EstadoCita.Cancelada or EstadoCita.NoAsistio);
}

/// <summary>
/// Datos editables de una cita. La hora va en LOCAL porque es lo que el
/// usuario escribe; la conversión a UTC la hace el repositorio, en un solo
/// lugar, para que no haya dos sitios donde equivocarse.
/// </summary>
public record CitaDatos(
    long ClienteId,
    long MedicoId,
    long? ProcedimientoId,
    DateTime FechaHoraLocal,
    int DuracionMinutos,
    string? Notas = null,
    EstadoCita Estado = EstadoCita.Programada);

/// <summary>Un hueco libre en la agenda de un médico, en hora local.</summary>
public record HuecoAgenda(DateTime InicioLocal, int DuracionMinutos)
{
    public DateTime FinLocal => InicioLocal.AddMinutes(DuracionMinutos);

    /// <summary>Cómo se lee en el combo: "9:00 am – 9:30 am".</summary>
    public string HoraTexto
    {
        get
        {
            var cultura = System.Globalization.CultureInfo.GetCultureInfo("es-DO");
            return $"{InicioLocal.ToString("h:mm tt", cultura)} – {FinLocal.ToString("h:mm tt", cultura)}";
        }
    }
}

/// <summary>
/// Un turno de la sala de espera.
///
/// <b>La fecha es día de negocio LOCAL, no UTC.</b> El turno 15 es el turno 15
/// de hoy para la gente sentada en la sala; si el corte fuera UTC, a las 8 de
/// la noche empezaría a numerar como si fuera mañana.
///
/// El paciente puede ser NULL: se le da el turno al que acaba de entrar por la
/// puerta, antes de saber quién es. Registrarlo viene después, y la factura
/// mucho después — son tres momentos distintos.
/// </summary>
public class Turno
{
    public long Id { get; set; }
    public DateOnly Fecha { get; set; }
    public int Numero { get; set; }
    public long? ClienteId { get; set; }
    public string? PacienteNombre { get; set; }
    public long? MedicoId { get; set; }
    public string? MedicoNombre { get; set; }
    /// <summary>
    /// Prefijo del médico ("YO"), resuelto por JOIN. Es lo que hace que en la
    /// sala se distinga YO-3 de IA-3 sin leer el nombre completo.
    /// </summary>
    public string? MedicoCodigoTurno { get; set; }
    /// <summary>Si venía con cita. Une la sala de espera con la agenda.</summary>
    public long? CitaId { get; set; }
    public EstadoTurno Estado { get; set; } = EstadoTurno.Esperando;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LlamadoAtUtc { get; set; }

    /// <summary>Sigue en la sala: se puede llamar.</summary>
    public bool EnEspera => Estado == EstadoTurno.Esperando;

    /// <summary>Ya terminó su paso por la sala, para bien o para mal.</summary>
    public bool EsFinal => Estado is EstadoTurno.Atendido or EstadoTurno.Ausente;
}

/// <summary>Lo que se pide al dar un turno. Todo opcional salvo el momento.</summary>
public record TurnoDatos(long? ClienteId = null, long? MedicoId = null, long? CitaId = null);

/// <summary>Conteo de la sala para el tablero de recepción.</summary>
public record ResumenSala(int Esperando, int Llamados, int Atendidos, int Ausentes)
{
    public int Total => Esperando + Llamados + Atendidos + Ausentes;
}

// =============================================================
// CLÍNICA (MED-100) — Ficha e historial del paciente
// =============================================================

/// <summary>
/// Una factura del paciente, vista desde su ficha.
///
/// Es más chica que <see cref="FacturaCompleta"/> a propósito: acá no hacen
/// falta las líneas ni el desglose del ITBIS, solo qué día vino, con quién,
/// cuánto se le cobró y cuánto puso él.
/// </summary>
public record FacturaDePaciente(
    long Id,
    string NumeroFactura,
    DateTime FechaEmisionUtc,
    string? MedicoNombre,
    decimal Total,
    decimal PacientePaga,
    decimal ArsCubierto,
    string? ArsNombre,
    MetodoPagoFactura MetodoPago,
    EstadoFactura Estado,
    string? Ncf)
{
    public bool Anulada => Estado == EstadoFactura.Anulada;
}

/// <summary>
/// Un procedimiento que se le hizo al paciente, sacado de las líneas de sus
/// facturas. Es lo más cerca del "qué le hicieron" que MED-100 puede mostrar
/// sin convertirse en expediente clínico: sale de lo que se COBRÓ, no de una
/// nota médica (CLAUDE.md §1.1).
/// </summary>
public record ProcedimientoDePaciente(
    DateTime FechaUtc,
    string Descripcion,
    int Cantidad,
    decimal Subtotal,
    string? MedicoNombre,
    string NumeroFactura,
    bool FacturaAnulada);

/// <summary>
/// Todo el paso del paciente por la clínica, para la pantalla "Ver detalles".
///
/// Va todo junto en una sola lectura porque la pregunta del mostrador es una
/// sola —"¿qué pasó con esta persona?"— y hacerla en cuatro viajes a la base
/// deja la pantalla llenándose por pedazos.
/// </summary>
public record HistorialPaciente(
    Cliente Paciente,
    IReadOnlyList<Cita> Citas,
    IReadOnlyList<Turno> Turnos,
    IReadOnlyList<FacturaDePaciente> Facturas,
    IReadOnlyList<ProcedimientoDePaciente> Procedimientos)
{
    /// <summary>Lo facturado que sigue en pie (las anuladas no cuentan).</summary>
    public decimal TotalFacturado => Facturas.Where(f => !f.Anulada).Sum(f => f.Total);

    /// <summary>Lo que puso el paciente de su bolsillo. Sin ARS, es igual al total.</summary>
    public decimal TotalPagadoPorElPaciente => Facturas.Where(f => !f.Anulada).Sum(f => f.PacientePaga);

    public decimal TotalCubiertoPorArs => Facturas.Where(f => !f.Anulada).Sum(f => f.ArsCubierto);

    public int CitasAtendidas => Citas.Count(c => c.Estado == EstadoCita.Atendida);
    public int CitasPerdidas => Citas.Count(c => c.Estado == EstadoCita.NoAsistio);

    /// <summary>Cuándo vino por última vez: la última cita atendida o la última factura.</summary>
    public DateTime? UltimaVisitaUtc
    {
        get
        {
            var deCitas = Citas.Where(c => c.Estado == EstadoCita.Atendida)
                               .Select(c => (DateTime?)c.FechaHoraUtc).Max();
            var deFacturas = Facturas.Where(f => !f.Anulada)
                                     .Select(f => (DateTime?)f.FechaEmisionUtc).Max();
            if (deCitas is null) return deFacturas;
            if (deFacturas is null) return deCitas;
            return deCitas > deFacturas ? deCitas : deFacturas;
        }
    }
}


// =============================================================
// CLÍNICA (MED-100) — Expediente digital del paciente
// =============================================================

/// <summary>
/// Un papel del expediente del paciente: la cédula, el carné de la ARS, un
/// consentimiento firmado, un referimiento, un estudio que trajo.
///
/// El ARCHIVO vive en el disco; esto es su FICHA. Guardar los bytes en la base
/// hincharía el dump hasta hacer inviable el respaldo diario.
///
/// ⚠️ Ley 172-13: lo que se archiva acá son datos de salud, sensibles. Por eso
/// cada alta, apertura y borrado queda en <c>auditoria</c>. Ojo con la
/// diferencia respecto a §1.1 del CLAUDE.md: la app sigue sin tener campos de
/// diagnóstico ni tratamiento — esto es un archivador de papeles escaneados.
/// </summary>
public class DocumentoPaciente
{
    public long Id { get; set; }
    public long ClienteId { get; set; }
    /// <summary>Nombre original del archivo, tal como lo ve el usuario.</summary>
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Relativa a la carpeta de expedientes: 'pacientes/&lt;id&gt;/&lt;doc&gt;_&lt;nombre&gt;'.</summary>
    public string RutaRelativa { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public TipoDocumentoPaciente Tipo { get; set; } = TipoDocumentoPaciente.Otro;
    public string? Notas { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Quién lo subió, resuelto por JOIN. Es el rastro que pide la ley.</summary>
    public string? SubidoPor { get; set; }

    /// <summary>Tamaño legible: "1.4 MB".</summary>
    public string TamanoTexto => TamanoBytes switch
    {
        < 1024 => $"{TamanoBytes} B",
        < 1024 * 1024 => $"{TamanoBytes / 1024d:0.#} KB",
        _ => $"{TamanoBytes / (1024d * 1024d):0.#} MB"
    };

    /// <summary>Familia del archivo, para el ícono y para saber con qué se abre.</summary>
    public string Familia => Extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".heic" or ".heif" => "Imagen",
        ".doc" or ".docx" or ".rtf" or ".odt" => "Word",
        ".xls" or ".xlsx" or ".csv" or ".ods" => "Excel",
        ".pdf" => "PDF",
        ".zip" or ".rar" or ".7z" => "Comprimido",
        ".txt" => "Texto",
        _ => "Archivo"
    };
}

/// <summary>Fila del almacén de expedientes: un paciente y cuántos papeles tiene.</summary>
public record ResumenExpediente(
    long ClienteId,
    string Nombre,
    string? Cedula,
    string? Telefono,
    int Documentos,
    DateTime? UltimoDocumentoUtc,
    int Citas,
    int Facturas,
    DateTime? UltimaVisitaUtc);

/// <summary>
/// Fila única (id = 1) de la tabla <c>licencia</c>: desde cuándo corre el demo
/// y si ya se activó con la llave del producto.
/// </summary>
public class Licencia
{
    /// <summary>Cuándo se abrió MED-100 por primera vez en este equipo.</summary>
    public DateTime InstaladaAtUtc { get; set; }

    public bool Activada { get; set; }
    public DateTime? ActivadaAtUtc { get; set; }

    /// <summary>Usuario de MED-100 que escribió la llave. Para el soporte.</summary>
    public string? ActivadaPor { get; set; }

    /// <summary>
    /// La última vez que la app arrancó. Si el reloj queda por detrás de esto,
    /// alguien lo movió: ver <see cref="EstadoLicencia.RelojAtrasado"/>.
    /// </summary>
    public DateTime UltimaAperturaUtc { get; set; }
}
