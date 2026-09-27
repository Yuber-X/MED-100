using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Consentimientos informados (015). Pedido de la clínica del 2026-09-21.
///
/// ACÁ VIVE EL CATÁLOGO DE TEXTOS, no los consentimientos firmados: el firmado
/// es el papel que el paciente firma a mano y que se escanea a su expediente
/// (decisión de Yuber, 2026-09-24). Por eso no hay nada que "marque" a un
/// paciente como consentido: eso sería afirmar algo que el sistema no vio.
///
/// Lo que sí queda es el rastro de cada impresión en <c>auditoria</c>: quién
/// imprimió qué consentimiento, para qué paciente y cuándo. Es lo que permite
/// decir después "este papel salió del sistema el día tal".
///
/// PERMISOS (2026-09-25):
///  * EDITAR exige <c>procedimientos</c>: la plantilla es parte del catálogo, y
///    quien define un procedimiento define lo que se firma para hacérselo.
///  * LEER e IMPRIMIR no exigen permiso propio: lo imprime quien atiende al
///    paciente, y es un papel en blanco que el paciente se lleva a firmar.
/// </summary>
public class ConsentimientoService
{
    private readonly ConsentimientoRepository _consentimientos;
    private readonly AuditoriaService _auditoria;

    public ConsentimientoService(ConsentimientoRepository consentimientos,
        AuditoriaService auditoria)
    {
        _consentimientos = consentimientos;
        _auditoria = auditoria;
    }

    public bool PuedeEditar => SesionActual.TienePermiso("procedimientos");

    public Task<List<Consentimiento>> ObtenerTodosAsync(bool soloActivos = false,
        CancellationToken ct = default) =>
        _consentimientos.ObtenerTodosAsync(soloActivos, ct);

    public Task<List<Consentimiento>> ObtenerParaProcedimientoAsync(long? procedimientoId,
        CancellationToken ct = default) =>
        _consentimientos.ObtenerParaProcedimientoAsync(procedimientoId, ct);

    public Task<Consentimiento?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _consentimientos.ObtenerPorIdAsync(id, ct);

    public async Task<long> CrearAsync(ConsentimientoDatos datos, CancellationToken ct = default)
    {
        var limpios = Validar(datos);
        var id = await _consentimientos.InsertarAsync(limpios, ct);

        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Consentimiento, id,
            $"Consentimiento creado: «{limpios.Titulo}»" +
            (limpios.ProcedimientoId is null ? " (general)" : ""), ct);

        Log.Information("Consentimiento {Id} creado ({Titulo})", id, limpios.Titulo);
        return id;
    }

    public async Task ActualizarAsync(long id, ConsentimientoDatos datos,
        CancellationToken ct = default)
    {
        var anterior = await _consentimientos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El consentimiento no existe o fue eliminado.");
        var limpios = Validar(datos);

        await _consentimientos.ActualizarAsync(id, limpios, ct);

        // Que el TEXTO cambió se anota, pero el texto viejo no se copia a la
        // auditoría: son párrafos enteros y llenarían la tabla que hay que poder
        // leer. Lo firmado sigue estando en el papel escaneado, que es lo que
        // vale.
        var detalle = $"Consentimiento modificado: «{limpios.Titulo}»";
        if (!string.Equals(anterior.Cuerpo, limpios.Cuerpo, StringComparison.Ordinal))
            detalle += " · cambió el texto";
        if (anterior.ProcedimientoId != limpios.ProcedimientoId)
            detalle += " · cambió el procedimiento al que aplica";
        if (anterior.Activo != limpios.Activo)
            detalle += limpios.Activo ? " · reactivado" : " · desactivado";

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Consentimiento, id,
            detalle, ct);
    }

    /// <summary>
    /// Baja lógica. No se borra de verdad: hay papeles firmados con este texto
    /// dando vueltas, y el día que alguien discuta qué firmó hay que poder
    /// leerlo.
    /// </summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        ExigirEdicion();

        var actual = await _consentimientos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El consentimiento no existe o ya fue eliminado.");

        await _consentimientos.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Consentimiento, id,
            $"Consentimiento dado de baja: «{actual.Titulo}»", ct);
    }

    /// <summary>
    /// Arma el papel para un paciente y deja el rastro de que se imprimió.
    ///
    /// La auditoría se escribe ACÁ y no en la pantalla a propósito: si mañana
    /// el consentimiento se imprime también desde la agenda o desde la caja, el
    /// rastro sigue saliendo solo.
    /// </summary>
    public async Task<ConsentimientoImpreso> PrepararImpresionAsync(long consentimientoId,
        Cliente paciente, string? medicoNombre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paciente);

        var plantilla = await _consentimientos.ObtenerPorIdAsync(consentimientoId, ct)
            ?? throw new InvalidOperationException("El consentimiento no existe o fue eliminado.");

        await _auditoria.RegistrarAsync(AccionAuditoria.Consultar, DbNames.Consentimiento,
            plantilla.Id,
            $"Consentimiento «{plantilla.Titulo}» impreso para {paciente.Nombre}" +
            (plantilla.ProcedimientoNombre is { } proc ? $" ({proc})" : ""), ct);

        Log.Information("Consentimiento {Id} impreso para el paciente {PacienteId}",
            plantilla.Id, paciente.Id);

        return new ConsentimientoImpreso(
            plantilla.Titulo,
            plantilla.Cuerpo,
            plantilla.ProcedimientoNombre,
            paciente.Nombre,
            paciente.Cedula,
            medicoNombre,
            DateTime.UtcNow);
    }

    private ConsentimientoDatos Validar(ConsentimientoDatos datos)
    {
        ExigirEdicion();

        var titulo = (datos.Titulo ?? string.Empty).Trim();
        var cuerpo = (datos.Cuerpo ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(titulo))
            throw new ArgumentException("Escribí el título del consentimiento.");
        if (titulo.Length > 150)
            throw new ArgumentException("El título no puede pasar de 150 caracteres.");
        if (string.IsNullOrWhiteSpace(cuerpo))
            throw new ArgumentException(
                "Escribí el texto del consentimiento: es lo que el paciente va a firmar.");

        return datos with { Titulo = titulo, Cuerpo = cuerpo };
    }

    private void ExigirEdicion()
    {
        if (!PuedeEditar)
            throw new UnauthorizedAccessException(
                "No tienes permiso para cambiar los textos de consentimiento. " +
                "Los edita quien administra el tarifario de procedimientos.");
    }
}
