using MED100.Models;

namespace MED100.Data;

/// <summary>Mapeo C# ↔ ENUMs de MySQL (nunca cadenas mágicas sueltas).</summary>
internal static class EnumMap
{
    public static string ADb(MetodoPagoFactura metodo) => metodo switch
    {
        MetodoPagoFactura.Efectivo => "efectivo",
        MetodoPagoFactura.Tarjeta => "tarjeta",
        MetodoPagoFactura.Transferencia => "transferencia",
        MetodoPagoFactura.Mixto => "mixto",
        _ => throw new ArgumentOutOfRangeException(nameof(metodo))
    };

    public static MetodoPagoFactura MetodoPagoDeDb(string valor) => valor switch
    {
        "efectivo" => MetodoPagoFactura.Efectivo,
        "tarjeta" => MetodoPagoFactura.Tarjeta,
        "transferencia" => MetodoPagoFactura.Transferencia,
        "mixto" => MetodoPagoFactura.Mixto,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "metodo_pago desconocido")
    };

    public static string ADb(EstadoFactura estado) => estado switch
    {
        EstadoFactura.Emitida => "emitida",
        EstadoFactura.Anulada => "anulada",
        _ => throw new ArgumentOutOfRangeException(nameof(estado))
    };

    public static EstadoFactura EstadoFacturaDeDb(string valor) => valor switch
    {
        "emitida" => EstadoFactura.Emitida,
        "anulada" => EstadoFactura.Anulada,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "estado desconocido")
    };

    public static string ADb(EstadoTurno estado) => estado switch
    {
        EstadoTurno.Esperando => "esperando",
        EstadoTurno.Llamado => "llamado",
        EstadoTurno.Atendido => "atendido",
        EstadoTurno.Ausente => "ausente",
        _ => throw new ArgumentOutOfRangeException(nameof(estado))
    };

    public static EstadoTurno EstadoTurnoDeDb(string valor) => valor switch
    {
        "esperando" => EstadoTurno.Esperando,
        "llamado" => EstadoTurno.Llamado,
        "atendido" => EstadoTurno.Atendido,
        "ausente" => EstadoTurno.Ausente,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "estado de turno desconocido")
    };

    public static string ADb(EstadoCita estado) => estado switch
    {
        EstadoCita.Programada => "programada",
        EstadoCita.Confirmada => "confirmada",
        EstadoCita.Atendida => "atendida",
        EstadoCita.Cancelada => "cancelada",
        EstadoCita.NoAsistio => "no_asistio",
        _ => throw new ArgumentOutOfRangeException(nameof(estado))
    };

    public static EstadoCita EstadoCitaDeDb(string valor) => valor switch
    {
        "programada" => EstadoCita.Programada,
        "confirmada" => EstadoCita.Confirmada,
        "atendida" => EstadoCita.Atendida,
        "cancelada" => EstadoCita.Cancelada,
        "no_asistio" => EstadoCita.NoAsistio,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "estado de cita desconocido")
    };

    public static string ADb(SexoPaciente sexo) => sexo switch
    {
        SexoPaciente.Femenino => "F",
        SexoPaciente.Masculino => "M",
        SexoPaciente.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(sexo))
    };

    public static SexoPaciente SexoDeDb(string valor) => valor switch
    {
        "F" => SexoPaciente.Femenino,
        "M" => SexoPaciente.Masculino,
        "otro" => SexoPaciente.Otro,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "sexo desconocido")
    };

    public static string ADb(TipoReferidor tipo) => tipo switch
    {
        TipoReferidor.Medico => "medico",
        TipoReferidor.Ars => "ars",
        TipoReferidor.Publicidad => "publicidad",
        TipoReferidor.Paciente => "paciente",
        TipoReferidor.Redes => "redes",
        TipoReferidor.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo))
    };

    public static TipoReferidor TipoReferidorDeDb(string valor) => valor switch
    {
        "medico" => TipoReferidor.Medico,
        "ars" => TipoReferidor.Ars,
        "publicidad" => TipoReferidor.Publicidad,
        "paciente" => TipoReferidor.Paciente,
        "redes" => TipoReferidor.Redes,
        "otro" => TipoReferidor.Otro,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "tipo de referidor desconocido")
    };

    public static string ADb(TipoProducto tipo) => tipo switch
    {
        TipoProducto.Insumo => "insumo",
        TipoProducto.Medicamento => "medicamento",
        TipoProducto.Material => "material",
        TipoProducto.Equipo => "equipo",
        TipoProducto.Limpieza => "limpieza",
        TipoProducto.Oficina => "oficina",
        TipoProducto.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo))
    };

    public static TipoProducto TipoProductoDeDb(string valor) => valor switch
    {
        "insumo" => TipoProducto.Insumo,
        "medicamento" => TipoProducto.Medicamento,
        "material" => TipoProducto.Material,
        "equipo" => TipoProducto.Equipo,
        "limpieza" => TipoProducto.Limpieza,
        "oficina" => TipoProducto.Oficina,
        "otro" => TipoProducto.Otro,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "tipo de producto desconocido")
    };

    public static string ADb(TipoDocumentoPaciente tipo) => tipo switch
    {
        TipoDocumentoPaciente.Identificacion => "identificacion",
        TipoDocumentoPaciente.Seguro => "seguro",
        TipoDocumentoPaciente.Consentimiento => "consentimiento",
        TipoDocumentoPaciente.Referimiento => "referimiento",
        TipoDocumentoPaciente.Estudio => "estudio",
        TipoDocumentoPaciente.Factura => "factura",
        TipoDocumentoPaciente.Otro => "otro",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo))
    };

    public static TipoDocumentoPaciente TipoDocumentoDeDb(string valor) => valor switch
    {
        "identificacion" => TipoDocumentoPaciente.Identificacion,
        "seguro" => TipoDocumentoPaciente.Seguro,
        "consentimiento" => TipoDocumentoPaciente.Consentimiento,
        "referimiento" => TipoDocumentoPaciente.Referimiento,
        "estudio" => TipoDocumentoPaciente.Estudio,
        "factura" => TipoDocumentoPaciente.Factura,
        "otro" => TipoDocumentoPaciente.Otro,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), valor, "tipo de documento desconocido")
    };
}
