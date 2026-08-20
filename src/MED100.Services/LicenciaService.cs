using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>
/// La licencia de esta instalación: 15 días de prueba, o completa si se
/// escribió la llave del producto.
///
/// Junta las dos copias de la fecha de instalación —la tabla <c>licencia</c> y
/// el ancla de <c>%ProgramData%</c>— y le pasa a <see cref="CalculadoraLicencia"/>
/// la más vieja de las dos. La regla de los días vive allá, que es puro
/// cálculo; acá está la parte que toca disco y base.
///
/// ⚠️ EL ESTADO SE CALCULA AL ARRANCAR Y NO SE VUELVE A MIRAR. Si la app queda
/// abierta pasada la medianoche del último día, sigue funcionando hasta que se
/// cierre. Es deliberado: cortar a mitad de un cobro, con el paciente delante,
/// sería peor que regalar unas horas.
/// </summary>
public class LicenciaService
{
    private readonly LicenciaRepository _repositorio;
    private readonly AnclaLicencia _ancla;
    private readonly AuditoriaService _auditoria;

    /// <summary>Avisa al shell para que refresque la pastilla del menú.</summary>
    public event Action? Cambio;

    public LicenciaService(LicenciaRepository repositorio, AnclaLicencia ancla, AuditoriaService auditoria)
    {
        _repositorio = repositorio;
        _ancla = ancla;
        _auditoria = auditoria;
    }

    /// <summary>Lo que se resolvió en el arranque. Antes de <see cref="EvaluarAsync"/>, demo lleno.</summary>
    public ResultadoLicencia Estado { get; private set; } =
        new(EstadoLicencia.Demo, CalculadoraLicencia.DiasDemo);

    public bool EsDemo => Estado.Estado == EstadoLicencia.Demo;
    public bool EstaActivada => Estado.Estado == EstadoLicencia.Completa;

    /// <summary>
    /// Resuelve la situación de la licencia. Se llama una vez, al arrancar,
    /// después de que la base está lista y antes de mostrar el login.
    /// </summary>
    public async Task<ResultadoLicencia> EvaluarAsync(CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;

        Licencia licencia;
        try
        {
            licencia = await _repositorio.ObtenerAsync(ahora, ct);
        }
        catch (Exception ex)
        {
            // La base no se dejó leer. Antes que dejar una clínica sin trabajar
            // por un problema de MySQL, se decide con el ancla del disco sola;
            // si tampoco está, se asume recién instalada.
            Log.Error(ex, "No se pudo leer la licencia de la base; se decide con el ancla del disco");
            var soloAncla = _ancla.Leer();
            Estado = CalculadoraLicencia.Evaluar(
                new LicenciaGuardada(soloAncla?.InicioUtc ?? ahora, soloAncla?.Activada ?? false, ahora),
                ahora);
            Cambio?.Invoke();
            return Estado;
        }

        var anclado = _ancla.Leer();

        // Si el ancla dice que esta PC empezó antes que lo que cree la base,
        // es que la base se borró. Manda la fecha vieja, en las dos copias.
        var inicio = CalculadoraLicencia.InicioReal(licencia.InstaladaAtUtc, anclado?.InicioUtc);
        if (inicio < licencia.InstaladaAtUtc)
        {
            await _repositorio.RetrasarInicioAsync(inicio, ct);
            Log.Warning("La base de datos era más nueva que el ancla de licencia: el demo sigue contando desde {Inicio:u}", inicio);
        }

        // Y al revés: si el ancla no está (primera vez, o alguien la borró),
        // se escribe con lo que diga la base.
        var activada = licencia.Activada;
        if (anclado is null || anclado.Value.InicioUtc != inicio || anclado.Value.Activada != activada)
            _ancla.Escribir(inicio, activada);

        Estado = CalculadoraLicencia.Evaluar(
            new LicenciaGuardada(inicio, activada, licencia.UltimaAperturaUtc), ahora);

        await _repositorio.RegistrarAperturaAsync(ahora, ct);

        Log.Information("Licencia: {Estado} ({Dias} días restantes)", Estado.Estado, Estado.DiasRestantes);
        Cambio?.Invoke();
        return Estado;
    }

    /// <summary>
    /// Intenta activar con el código escrito. Devuelve false si el código no
    /// es el bueno — sin decir por qué: distinguir "mal escrito" de "no es la
    /// llave" solo le sirve a quien está probando llaves.
    /// </summary>
    public async Task<bool> ActivarAsync(string? codigo, CancellationToken ct = default)
    {
        if (!CodigoLicencia.EsValido(codigo))
        {
            Log.Warning("Intento de activación con un código que no corresponde");
            return false;
        }

        var ahora = DateTime.UtcNow;
        var usuario = SesionActual.HaySesionActiva ? SesionActual.Username : null;

        await _repositorio.ActivarAsync(usuario, ahora, ct);
        _ancla.Escribir((await _repositorio.ObtenerAsync(ahora, ct)).InstaladaAtUtc, activada: true);

        Estado = new ResultadoLicencia(EstadoLicencia.Completa, 0);
        Log.Information("MED-100 activado (llave del producto) por {Usuario}", usuario ?? "antes del login");

        // La activación se audita solo si hay alguien con sesión abierta: la
        // ventana de arranque aparece ANTES del login y la auditoría exige
        // usuario. Queda en el log de Serilog en los dos casos.
        if (SesionActual.HaySesionActiva)
        {
            try
            {
                await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, "licencia", 1,
                    "Se activó MED-100 con la llave del producto", ct);
            }
            catch (Exception ex)
            {
                // Que falle la auditoría no puede deshacer una activación válida
                Log.Warning(ex, "No se pudo auditar la activación de la licencia");
            }
        }

        Cambio?.Invoke();
        return true;
    }
}
