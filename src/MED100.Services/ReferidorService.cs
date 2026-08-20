using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Catálogo de procedencias ("registro de proveniento"): quién manda a los
/// pacientes a la clínica.
///
/// Se gestiona con el mismo permiso que los pacientes (<c>clientes_editar</c>)
/// a propósito: la recepcionista aprende de dónde viene el paciente mientras
/// lo registra, y mandarla a otra pantalla para dar de alta "Dr. Pérez" haría
/// que termine escribiéndolo en las notas y el dato se pierda.
/// </summary>
public class ReferidorService
{
    private readonly ReferidorRepository _referidores;
    private readonly AuditoriaService _auditoria;

    public ReferidorService(ReferidorRepository referidores, AuditoriaService auditoria)
    {
        _referidores = referidores;
        _auditoria = auditoria;
    }

    public Task<List<Referidor>> ObtenerTodosAsync(CancellationToken ct = default) =>
        _referidores.ObtenerTodosAsync(ct);

    public Task<List<Referidor>> ObtenerActivosAsync(CancellationToken ct = default) =>
        _referidores.ObtenerActivosAsync(ct);

    public Task<Referidor?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _referidores.ObtenerPorIdAsync(id, ct);

    /// <summary>
    /// Traduce lo que se escribió en el formulario del paciente a un id.
    ///
    /// - Vacío → null (el paciente llegó por su cuenta, que es lo más común).
    /// - Nombre que ya existe con ese tipo → se reutiliza. Nunca se duplica.
    /// - Nombre nuevo → se crea sobre la marcha.
    ///
    /// Esto es lo que evita terminar con "Dr. Pérez", "Dr Perez" y "dr. perez"
    /// como tres procedencias distintas en el reporte.
    /// </summary>
    public async Task<long?> ResolverAsync(string? nombre, TipoReferidor tipo,
        CancellationToken ct = default)
    {
        var limpio = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
        if (limpio is null)
            return null;

        var existente = await _referidores.BuscarPorNombreAsync(limpio, tipo, ct);
        if (existente is not null)
            return existente.Id;

        return await CrearAsync(new ReferidorDatos(limpio, tipo), ct);
    }

    public async Task<long> CrearAsync(ReferidorDatos datos, CancellationToken ct = default)
    {
        var limpios = Validar(datos);
        var id = await _referidores.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Referidor, id,
            $"Procedencia creada: {limpios.Nombre} ({Etiqueta(limpios.Tipo)})", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, ReferidorDatos datos, CancellationToken ct = default)
    {
        var anterior = await _referidores.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("La procedencia no existe.");
        var limpios = Validar(datos);
        await _referidores.ActualizarAsync(id, limpios, ct);

        var detalle = $"Procedencia modificada: {limpios.Nombre}";
        if (anterior.Tipo != limpios.Tipo)
            detalle += $" · tipo {Etiqueta(anterior.Tipo)} → {Etiqueta(limpios.Tipo)}";
        if (anterior.Activo != limpios.Activo)
            detalle += limpios.Activo ? " · reactivada" : " · desactivada";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Referidor, id, detalle, ct);
    }

    public Task<int> ContarPacientesAsync(long id, CancellationToken ct = default) =>
        _referidores.ContarPacientesAsync(id, ct);

    /// <summary>Nombre en español para la UI y la auditoría.</summary>
    public static string Etiqueta(TipoReferidor tipo) => tipo switch
    {
        TipoReferidor.Medico => "Médico",
        TipoReferidor.Ars => "ARS / seguro",
        TipoReferidor.Publicidad => "Publicidad",
        TipoReferidor.Paciente => "Otro paciente",
        TipoReferidor.Redes => "Redes sociales",
        TipoReferidor.Otro => "Otro",
        _ => tipo.ToString()
    };

    private static ReferidorDatos Validar(ReferidorDatos datos)
    {
        if (!SesionActual.TienePermiso("clientes_editar"))
            throw new InvalidOperationException("No tienes permiso para registrar procedencias.");

        var nombre = datos.Nombre?.Trim();
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre de la procedencia es obligatorio.");
        if (nombre.Length > 150)
            throw new ArgumentException("El nombre de la procedencia no puede pasar de 150 caracteres.");

        var notas = string.IsNullOrWhiteSpace(datos.Notas) ? null : datos.Notas.Trim();
        if (notas is { Length: > 250 })
            throw new ArgumentException("Las notas de la procedencia no pueden pasar de 250 caracteres.");

        return datos with { Nombre = nombre, Notas = notas };
    }
}
