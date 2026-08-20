using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila del tablero de la sala.</summary>
public record TurnoFila(Turno Turno, string Etiqueta)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public long Id => Turno.Id;
    public int Numero => Turno.Numero;
    public string PacienteTexto => Turno.PacienteNombre ?? "Sin registrar";
    public bool TienePaciente => Turno.ClienteId is not null;
    public long? MedicoId => Turno.MedicoId;
    public string MedicoTexto => Turno.MedicoNombre ?? "—";
    public string EstadoTexto => TurnoService.EtiquetaEstado(Turno.Estado);
    public bool EnEspera => Turno.EnEspera;
    public bool EsFinal => Turno.EsFinal;

    public string LlegadaTexto =>
        FechaNegocio.AUtcLocal(Turno.CreatedAtUtc).ToString("h:mm tt", CulturaRd);

    /// <summary>
    /// Cuánto lleva esperando, o cuánto esperó si ya lo llamaron. Es el número
    /// que hace visible el problema antes de que alguien se queje.
    /// </summary>
    public string EsperaTexto
    {
        get
        {
            var desde = FechaNegocio.AUtcLocal(Turno.CreatedAtUtc);
            var hasta = Turno.LlamadoAtUtc is { } llamado
                ? FechaNegocio.AUtcLocal(llamado)
                : FechaNegocio.AhoraLocal();
            var minutos = (int)(hasta - desde).TotalMinutes;
            if (minutos < 1) return "recién";
            if (minutos < 60) return $"{minutos} min";
            return $"{minutos / 60} h {minutos % 60} min";
        }
    }
}

/// <summary>
/// Sala de espera: dar turnos, llamar al siguiente y cerrarlos.
///
/// Nada de esto exige paciente. El número se entrega en la puerta, el registro
/// viene después y la factura mucho después — son tres momentos distintos
/// (CLAUDE.md §1.3.4).
/// </summary>
public partial class TurnosViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly TurnoService _turnos;
    private readonly MedicoService _medicos;
    private readonly ClienteService _pacientes;
    private readonly ConfiguracionNegocioService _negocio;
    private readonly IDialogService _dialogos;

    private IReadOnlyList<Cliente> _todosLosPacientes = [];

    /// <summary>El shell imprime el papelito: los ViewModels no conocen la impresora.</summary>
    public event Action<Turno, string, bool>? ImpresionSolicitada;

    public TurnosViewModel(TurnoService turnos, MedicoService medicos, ClienteService pacientes,
        ConfiguracionNegocioService negocio, IDialogService dialogos)
    {
        _turnos = turnos;
        _medicos = medicos;
        _pacientes = pacientes;
        _negocio = negocio;
        _dialogos = dialogos;
    }

    public ObservableCollection<TurnoFila> Turnos { get; } = [];
    public ObservableCollection<OpcionCatalogo> Medicos { get; } = [];
    public ObservableCollection<Cliente> PacientesSugeridos { get; } = [];

    /// <summary>
    /// Los turnos que le tocan al MISMO médico del turno elegido, sin mezclar
    /// con los de otros (pedido de Yuber 2026-08-14). En una sala con tres
    /// médicos atendiendo, la lista general no contesta "¿a quién le toca
    /// ahora con la doctora?" — esta sí.
    /// </summary>
    public ObservableCollection<TurnoFila> TurnosDelMedico { get; } = [];

    [ObservableProperty] private TurnoFila? _seleccionado;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private string _resumen = string.Empty;
    [ObservableProperty] private string _enPantalla = "—";
    [ObservableProperty] private string _mensajeError = string.Empty;

    // ---------- Alta de turno ----------
    //
    // El panel de "dar turno" arranca CERRADO detrás de un botón: el panel
    // derecho lo ocupa ahora la ficha del médico y sus turnos, que es lo que
    // se mira todo el día. Dar un turno son dos clics y pasa cada tanto.
    [ObservableProperty] private bool _dandoTurno;
    [ObservableProperty] private string _busquedaPaciente = string.Empty;
    [ObservableProperty] private Cliente? _pacienteSeleccionado;
    [ObservableProperty] private OpcionCatalogo? _medicoSeleccionado;
    [ObservableProperty] private string _mensajePacientes = string.Empty;

    // ---------- Ficha del médico del turno elegido ----------
    [ObservableProperty] private string _medicoDelTurno = string.Empty;
    [ObservableProperty] private string _especialidadDelTurno = string.Empty;
    [ObservableProperty] private string _estadoDelMedico = string.Empty;
    [ObservableProperty] private string _diasDelMedico = string.Empty;
    [ObservableProperty] private string _codigoDelMedico = string.Empty;
    [ObservableProperty] private string _resumenDelMedico = string.Empty;
    [ObservableProperty] private bool _hayMedicoEnElTurno;

    private IReadOnlyList<MedicoDisponibilidad> _disponibilidad = [];

    public bool HaySeleccion => Seleccionado is not null;
    public bool PuedeCerrarSeleccionado => Seleccionado is { EsFinal: false };
    public bool PuedeAsignarPaciente => Seleccionado is { TienePaciente: false };

    partial void OnSeleccionadoChanged(TurnoFila? value)
    {
        OnPropertyChanged(nameof(HaySeleccion));
        OnPropertyChanged(nameof(PuedeCerrarSeleccionado));
        OnPropertyChanged(nameof(PuedeAsignarPaciente));
        ActualizarFichaDelMedico();
    }

    /// <summary>
    /// Llena la ficha del médico del turno elegido y la lista de SUS turnos.
    /// Sale de lo que ya está cargado en memoria: no vuelve a la base porque
    /// se dispara con cada clic en la tabla.
    /// </summary>
    private void ActualizarFichaDelMedico()
    {
        TurnosDelMedico.Clear();
        HayMedicoEnElTurno = Seleccionado?.MedicoId is not null;

        if (Seleccionado?.MedicoId is not { } medicoId)
        {
            MedicoDelTurno = EspecialidadDelTurno = EstadoDelMedico =
                DiasDelMedico = CodigoDelMedico = ResumenDelMedico = string.Empty;
            return;
        }

        foreach (var fila in Turnos.Where(t => t.MedicoId == medicoId))
            TurnosDelMedico.Add(fila);

        var esperando = TurnosDelMedico.Count(t => t.EnEspera);
        var cerrados = TurnosDelMedico.Count(t => t.EsFinal);
        ResumenDelMedico = $"{TurnosDelMedico.Count} turno(s) hoy · " +
                           $"{esperando} esperando · {cerrados} cerrado(s)";

        var info = _disponibilidad.FirstOrDefault(d => d.Medico.Id == medicoId);
        MedicoDelTurno = info?.Medico.Nombre ?? Seleccionado.MedicoTexto;
        EspecialidadDelTurno = string.IsNullOrWhiteSpace(info?.Medico.Especialidad)
            ? "Sin especialidad cargada" : info!.Medico.Especialidad!;
        EstadoDelMedico = info?.Detalle ?? string.Empty;
        DiasDelMedico = info is null
            ? string.Empty
            : DisponibilidadMedicos.ResumirDias(info.DiasQueAtiende);
        CodigoDelMedico = info?.Medico.CodigoTurno ?? "—";
    }

    partial void OnBusquedaPacienteChanged(string value) => FiltrarPacientes();

    public async Task RefrescarAsync()
    {
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;
            await CargarCatalogosAsync();

            var previo = Seleccionado?.Id;
            var turnos = await _turnos.ObtenerDelDiaAsync();

            // La disponibilidad se relee acá y no en cada clic: la ficha del
            // médico se arma en memoria mientras la recepción navega la tabla.
            _disponibilidad = await _medicos.ObtenerDisponibilidadAsync();

            Turnos.Clear();
            foreach (var t in turnos)
                Turnos.Add(new TurnoFila(t, _turnos.Etiqueta(t)));

            var r = await _turnos.ResumenAsync();
            Resumen = r.Total == 0
                ? "Todavía no se dio ningún turno hoy"
                : $"{r.Esperando} esperando · {r.Llamados} llamado(s) · " +
                  $"{r.Atendidos} atendido(s) · {r.Ausentes} ausente(s)";

            // El último llamado es lo que iría en la pantalla de la sala.
            var ultimoLlamado = turnos
                .Where(t => t.LlamadoAtUtc is not null)
                .OrderByDescending(t => t.LlamadoAtUtc)
                .FirstOrDefault();
            EnPantalla = ultimoLlamado is null ? "—" : _turnos.Etiqueta(ultimoLlamado);

            Seleccionado = Turnos.FirstOrDefault(t => t.Id == previo);
            // Si la selección sobrevivió, OnSeleccionadoChanged no se dispara:
            // hay que rearmar la ficha a mano con los turnos recién leídos.
            ActualizarFichaDelMedico();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la sala de espera");
            _dialogos.MostrarError("Turnos", $"No se pudo cargar la sala.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async Task CargarCatalogosAsync()
    {
        if (Medicos.Count == 0)
        {
            Medicos.Add(new OpcionCatalogo(null, "Sin médico asignado"));
            foreach (var m in await _medicos.ObtenerActivosAsync())
                Medicos.Add(new OpcionCatalogo(m.Id, m.Nombre));
            MedicoSeleccionado ??= Medicos[0];
        }
        _todosLosPacientes = await _pacientes.ObtenerTodosAsync();
        FiltrarPacientes();
    }

    /// <summary>Cuántos pacientes se ofrecen de una en el combo.</summary>
    private const int TopePacientes = 50;

    private void FiltrarPacientes()
    {
        var filtro = BusquedaPaciente.Trim();
        PacientesSugeridos.Clear();

        // Con la caja vacía se listan los pacientes registrados: antes había
        // que escribir dos letras y el combo en blanco parecía roto.
        var visibles = filtro.Length == 0
            ? _todosLosPacientes
            : _todosLosPacientes
                .Where(p => p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                            (p.Cedula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (p.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var p in visibles.Take(TopePacientes))
            PacientesSugeridos.Add(p);

        MensajePacientes = _todosLosPacientes.Count == 0
            ? "Todavía no hay pacientes registrados. El turno se puede dar igual, en blanco."
            : PacientesSugeridos.Count == 0
                ? "Ningún paciente coincide con esa búsqueda."
                : _todosLosPacientes.Count > PacientesSugeridos.Count
                    ? $"Mostrando {PacientesSugeridos.Count} de {_todosLosPacientes.Count}."
                    : string.Empty;
    }

    // ---------- Comandos ----------

    /// <summary>Abre el panel de dar turno, limpio para la persona que llegó.</summary>
    [RelayCommand]
    private void AbrirDarTurno()
    {
        BusquedaPaciente = string.Empty;
        PacienteSeleccionado = null;
        MedicoSeleccionado = Medicos.FirstOrDefault();
        MensajeError = string.Empty;
        FiltrarPacientes();
        DandoTurno = true;
    }

    [RelayCommand]
    private void CerrarDarTurno()
    {
        DandoTurno = false;
        MensajeError = string.Empty;
    }

    /// <summary>
    /// Da el siguiente turno. Es el botón grande: se aprieta con el paciente
    /// parado en el mostrador, así que no pide nada obligatorio.
    /// </summary>
    [RelayCommand]
    private async Task DarTurnoAsync()
    {
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;

            var turno = await _turnos.DarAsync(new TurnoDatos(
                PacienteSeleccionado?.Id, MedicoSeleccionado?.Id));

            // Se limpia para el siguiente: dejar los datos del anterior haría
            // que el turno de la próxima persona salga con el paciente de atrás.
            BusquedaPaciente = string.Empty;
            PacienteSeleccionado = null;
            MedicoSeleccionado = Medicos.FirstOrDefault();
            // El panel se cierra solo: el papelito ya salió y el mostrador
            // vuelve a mostrar la sala, que es lo que hay que mirar.
            DandoTurno = false;

            ImpresionSolicitada?.Invoke(turno, _turnos.Etiqueta(turno),
                _negocio.Actual.TurnoImprimirAuto);

            await RefrescarAsync();
            Seleccionado = Turnos.FirstOrDefault(t => t.Id == turno.Id);
        }
        catch (InvalidOperationException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error dando un turno");
            _dialogos.MostrarError("Turnos", $"No se pudo dar el turno.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task LlamarSiguienteAsync()
    {
        try
        {
            var turno = await _turnos.LlamarSiguienteAsync();
            if (turno is null)
            {
                _dialogos.Informar("Sala de espera", "No hay nadie esperando.");
                return;
            }
            await RefrescarAsync();
            Seleccionado = Turnos.FirstOrDefault(t => t.Id == turno.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error llamando al siguiente turno");
            _dialogos.MostrarError("Turnos", $"No se pudo llamar al siguiente.\n\n{ex.Message}");
        }
    }

    /// <summary>Llama a uno puntual, salteando el orden (pasa todo el tiempo).</summary>
    [RelayCommand]
    private async Task LlamarAsync()
    {
        if (Seleccionado is null)
            return;
        try
        {
            await _turnos.LlamarAsync(Seleccionado.Id);
            await RefrescarAsync();
        }
        catch (InvalidOperationException ex)
        {
            _dialogos.MostrarError("Llamar turno", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error llamando el turno");
            _dialogos.MostrarError("Turnos", $"No se pudo llamar el turno.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private Task MarcarAtendidoAsync() => CerrarAsync(EstadoTurno.Atendido);

    [RelayCommand]
    private Task MarcarAusenteAsync() => CerrarAsync(EstadoTurno.Ausente);

    private async Task CerrarAsync(EstadoTurno estado)
    {
        if (Seleccionado is null)
            return;
        try
        {
            await _turnos.CerrarAsync(Seleccionado.Id, estado);
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cerrando el turno");
            _dialogos.MostrarError("Turnos", $"No se pudo cerrar el turno.\n\n{ex.Message}");
        }
    }

    /// <summary>Le pone paciente a un turno que se dio en blanco en la puerta.</summary>
    [RelayCommand]
    private async Task AsignarPacienteAsync()
    {
        if (Seleccionado is null)
            return;
        if (PacienteSeleccionado is null)
        {
            MensajeError = "Buscá y elegí el paciente antes de asignarlo.";
            return;
        }

        try
        {
            MensajeError = string.Empty;
            await _turnos.AsignarPacienteAsync(Seleccionado.Id, PacienteSeleccionado.Id);
            BusquedaPaciente = string.Empty;
            PacienteSeleccionado = null;
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error asignando el paciente al turno");
            _dialogos.MostrarError("Turnos", $"No se pudo asignar el paciente.\n\n{ex.Message}");
        }
    }

    /// <summary>Reimprime el papelito: se pierde, se moja, se rompe.</summary>
    [RelayCommand]
    private async Task ReimprimirAsync()
    {
        if (Seleccionado is null)
            return;
        var turno = await _turnos.ObtenerPorIdAsync(Seleccionado.Id);
        if (turno is null)
            return;
        // Siempre con vista previa: es una reimpresión a pedido, no el flujo
        // automático del mostrador.
        ImpresionSolicitada?.Invoke(turno, _turnos.Etiqueta(turno), false);
    }
}
