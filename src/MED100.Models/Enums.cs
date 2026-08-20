namespace MED100.Models;

/// <summary>Acción registrada en auditoría. Coincide con ENUM auditoria.accion.</summary>
public enum AccionAuditoria
{
    Crear,
    Modificar,
    Eliminar,
    Consultar,
    Login,
    Logout,
    Anular
}

/// <summary>Método de pago de una factura. Coincide con ENUM factura.metodo_pago.</summary>
public enum MetodoPagoFactura
{
    Efectivo,
    Tarjeta,
    Transferencia,
    Mixto
}

/// <summary>Estado de una factura: nunca se elimina, solo se anula.</summary>
public enum EstadoFactura
{
    Emitida,
    Anulada
}

/// <summary>Redondeo del TOTAL de la venta. Coincide con ENUM configuracion_negocio.redondeo.</summary>
public enum ModoRedondeo
{
    Centavo,   // al centavo más cercano (default)
    Peso,      // al peso más cercano
    Arriba     // al peso, siempre hacia arriba
}

/// <summary>Formato del número de factura. Coincide con ENUM configuracion_negocio.factura_formato.</summary>
public enum FormatoFactura
{
    Simple,    // F-0001
    ConAnio    // F-2026-0001
}

/// <summary>
/// Estado de una cita. Coincide con ENUM cita.estado.
///
/// <c>NoAsistio</c> no es lo mismo que <c>Cancelada</c>: cancelar es que
/// avisaron, no asistir es que dejaron el hueco vacío sin avisar. La clínica
/// necesita poder distinguirlos para saber a quién le pasa seguido.
/// </summary>
public enum EstadoCita
{
    Programada,
    Confirmada,
    Atendida,
    Cancelada,
    NoAsistio
}

/// <summary>
/// Estado de un turno de la sala de espera. Coincide con ENUM turno.estado.
///
/// <c>Ausente</c> es el que se llamó y no contestó. Se separa de
/// <c>Atendido</c> porque es lo que deja ver, al final del día, cuánta gente
/// se cansó de esperar y se fue.
/// </summary>
public enum EstadoTurno
{
    Esperando,
    Llamado,
    Atendido,
    Ausente
}

/// <summary>Sexo del paciente. Coincide con ENUM cliente.sexo. Opcional: puede no registrarse.</summary>
public enum SexoPaciente
{
    Femenino,
    Masculino,
    Otro
}

/// <summary>
/// De dónde viene el paciente ("registro de proveniento"). Coincide con
/// ENUM referidor.tipo. El tipo importa porque es como se agrupa el reporte:
/// no es lo mismo saber que llegaron 40 pacientes por publicidad que saber
/// que 40 los mandó el mismo médico.
/// </summary>
public enum TipoReferidor
{
    Medico,
    Ars,
    Publicidad,
    Paciente,
    Redes,
    Otro
}

/// <summary>
/// Semáforo de caducidad calculado en tiempo real (no se persiste).
/// Umbrales por definir con el cliente en Fase 2 (defaults del POS-400).
/// </summary>
public enum SemaforoCaducidad
{
    Verde,      // lejos de caducar
    Amarillo,   // se acerca
    Naranja,    // muy cerca
    Rojo        // caducado o al límite
}

/// <summary>
/// Qué clase de cosa es un producto del almacén (pedido de Yuber 2026-08-14).
/// Coincide con ENUM producto.tipo.
///
/// En una clínica el almacén no es solo "insumos": hay medicamentos que se
/// dispensan al paciente, material que se esteriliza y se reusa, equipos que
/// no se venden y artículos de limpieza. Agruparlos es lo que permite
/// preguntarle al almacén por familia en vez de leer la lista entera.
/// </summary>
public enum TipoProducto
{
    /// <summary>Gasas, jeringas, guantes: lo que se consume con el paciente.</summary>
    Insumo,
    /// <summary>Lo que se dispensa o se aplica y tiene lote y vencimiento.</summary>
    Medicamento,
    /// <summary>Material médico que se esteriliza y se vuelve a usar.</summary>
    Material,
    /// <summary>Equipos. No se venden; están para saber qué hay.</summary>
    Equipo,
    /// <summary>Desinfectantes y artículos de aseo.</summary>
    Limpieza,
    /// <summary>Papelería y consumibles de oficina.</summary>
    Oficina,
    Otro
}

/// <summary>
/// Para qué sirve el papel dentro del expediente del paciente.
/// Coincide con ENUM documento_paciente.tipo.
/// </summary>
public enum TipoDocumentoPaciente
{
    Otro,
    /// <summary>Cédula o pasaporte.</summary>
    Identificacion,
    /// <summary>Carné de la ARS, autorizaciones del seguro.</summary>
    Seguro,
    /// <summary>Consentimiento informado firmado.</summary>
    Consentimiento,
    /// <summary>Referimiento de otro médico o de otra clínica.</summary>
    Referimiento,
    /// <summary>Estudio o resultado que el paciente TRAJO. Ver Ley 172-13.</summary>
    Estudio,
    /// <summary>Factura o comprobante escaneado.</summary>
    Factura
}

/// <summary>
/// En qué situación está la licencia de esta instalación.
/// La calcula <c>CalculadoraLicencia</c>; la app la consulta al arrancar.
/// </summary>
public enum EstadoLicencia
{
    /// <summary>Activada con la llave del producto. Sin límite de tiempo.</summary>
    Completa,

    /// <summary>Prueba de 15 días, todavía con días por delante.</summary>
    Demo,

    /// <summary>Se acabaron los 15 días y nadie escribió la llave.</summary>
    Vencida,

    /// <summary>
    /// El reloj de la PC está más atrás que la última vez que se abrió la app.
    /// Se trata como vencida: es lo que pasa cuando alguien atrasa la fecha
    /// para estirar el demo. Se arregla escribiendo la llave, no la hora.
    /// </summary>
    RelojAtrasado
}
