using System.Windows.Threading;
using MED100.Common;
using MED100.Services;
using Serilog;

namespace MED100.App;

/// <summary>
/// Cierre automático del cuadre de caja (pedido Yuber 2026-07-12).
///
/// Revisa cada minuto si ya pasó la hora configurada del día de negocio; si es
/// así, cierra los turnos pendientes de TODOS los cajeros y lo anota para no
/// repetirlo. Si la app estaba cerrada a esa hora, el cierre ocurre al volver
/// a abrirla (mismo día). Solo actúa con permiso 'cuadre_todos' (Admin/Supervisor).
///
/// NUNCA lanza: un fallo del cierre automático no puede tumbar la aplicación.
/// </summary>
public class CierreAutomatico
{
    private readonly CuadreService _cuadres;
    private readonly AjustesLocales _ajustes;
    private readonly DispatcherTimer _timer;
    private bool _ejecutando;

    public CierreAutomatico(CuadreService cuadres, AjustesLocales ajustes)
    {
        _cuadres = cuadres;
        _ajustes = ajustes;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) => await RevisarAsync();
    }

    public void Iniciar()
    {
        _timer.Start();
        _ = RevisarAsync();   // por si la app se abre después de la hora del cierre
    }

    public void Detener() => _timer.Stop();

    private async Task RevisarAsync()
    {
        if (_ejecutando || !_ajustes.CierreAutomaticoActivo)
            return;
        if (!SesionActual.HaySesionActiva || !CuadreService.PuedeVerTodos)
            return;

        var hoy = FechaNegocio.Hoy;
        if (_ajustes.UltimoCierreAutomatico == hoy)
            return;   // ya se cerró hoy

        if (FechaNegocio.AhoraLocal().Hour < _ajustes.CierreAutomaticoHora)
            return;   // todavía no es la hora

        _ejecutando = true;
        try
        {
            var cerrados = await _cuadres.CerrarPendientesDelDiaAsync(hoy);

            _ajustes.UltimoCierreAutomatico = hoy;
            _ajustes.Guardar();

            if (cerrados > 0)
                Log.Information("Cierre automático: {Cerrados} turno(s) cerrados del {Fecha}",
                    cerrados, hoy);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falló el cierre automático de caja");
        }
        finally
        {
            _ejecutando = false;
        }
    }
}
