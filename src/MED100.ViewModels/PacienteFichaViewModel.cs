using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Una cita en el historial del paciente.</summary>
public record CitaHistorialFila(Cita Cita)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Cita.FechaHoraUtc).ToString("dd/MM/yyyy h:mm tt", CulturaRd);
    public string Medico => Cita.MedicoNombre;
    public string MotivoTexto => Cita.ProcedimientoNombre ?? "Consulta general";
    public string EstadoTexto => AgendaMedico.EtiquetaEstado(Cita.Estado);
    public bool Atendida => Cita.Estado == EstadoCita.Atendida;
    public bool Perdida => Cita.Estado == EstadoCita.NoAsistio;
    public string CobradaTexto => Cita.FacturaId is null ? "—" : "Sí";
}

/// <summary>Un turno de sala en el historial.</summary>
public record TurnoHistorialFila(Turno Turno, string Etiqueta)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public string FechaTexto => Turno.Fecha.ToString("dd/MM/yyyy", CulturaRd);
    public string HoraTexto =>
        FechaNegocio.AUtcLocal(Turno.CreatedAtUtc).ToString("h:mm tt", CulturaRd);
    public string MedicoTexto => Turno.MedicoNombre ?? "—";
    public string EstadoTexto => TurnoService.EtiquetaEstado(Turno.Estado);
}

/// <summary>Una factura en el historial.</summary>
public record FacturaHistorialFila(FacturaDePaciente Factura)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public long Id => Factura.Id;
    public string Numero => Factura.NumeroFactura;
    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Factura.FechaEmisionUtc).ToString("dd/MM/yyyy h:mm tt", CulturaRd);
    public string MedicoTexto => Factura.MedicoNombre ?? "—";
    public string TotalTexto => Factura.Total.ToString("N2", CulturaRd);
    public string PacientePagaTexto => Factura.PacientePaga.ToString("N2", CulturaRd);
    /// <summary>La ARS y cuánto puso ella. Sin seguro, un guion.</summary>
    public string ArsTexto => string.IsNullOrWhiteSpace(Factura.ArsNombre)
        ? "—"
        : $"{Factura.ArsNombre} ({Factura.ArsCubierto.ToString("N2", CulturaRd)})";
    public string NcfTexto => string.IsNullOrWhiteSpace(Factura.Ncf) ? "—" : Factura.Ncf!;
    public string EstadoTexto => Factura.Anulada ? "Anulada" : "Emitida";
    public bool Anulada => Factura.Anulada;
}

/// <summary>Un procedimiento cobrado, en el historial.</summary>
public record ProcedimientoHistorialFila(ProcedimientoDePaciente Procedimiento)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Procedimiento.FechaUtc).ToString("dd/MM/yyyy", CulturaRd);
    public string Descripcion => Procedimiento.Descripcion;
    public int Cantidad => Procedimiento.Cantidad;
    public string MedicoTexto => Procedimiento.MedicoNombre ?? "—";
    public string MontoTexto => Procedimiento.Subtotal.ToString("N2", CulturaRd);
    public string FacturaTexto => Procedimiento.NumeroFactura;
    public bool Anulada => Procedimiento.FacturaAnulada;
}

/// <summary>
/// La ficha completa del paciente: sus datos arriba y su paso por la clínica
/// abajo — citas, turnos de sala, facturas y procedimientos cobrados.
///
/// Pedido de Yuber (2026-08-14): "un ver detalles para visualizar todo del
/// paciente y su historial". Contesta de una vez las preguntas del mostrador:
/// cuándo vino, con qué médico, qué se le hizo y si quedó algo sin cobrar.
///
/// <b>No es el expediente clínico.</b> Lo que se ve acá salió de la agenda y de
/// la caja; diagnósticos y tratamientos no se guardan en MED-100 (CLAUDE.md §1.1).
/// </summary>
public partial class PacienteFichaViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly ClienteService _pacientes;
    private readonly TurnoService _turnos;
    private readonly IDialogService _dialogos;

    public event Action? Cerrado;
    public event Action<long>? EdicionSolicitada;
    /// <summary>El shell lleva a la agenda con este paciente ya elegido.</summary>
    public event Action<long>? CitaSolicitada;

    public PacienteFichaViewModel(ClienteService pacientes, TurnoService turnos,
        IDialogService dialogos)
    {
        _pacientes = pacientes;
        _turnos = turnos;
        _dialogos = dialogos;
    }

    public ObservableCollection<CitaHistorialFila> Citas { get; } = [];
    public ObservableCollection<TurnoHistorialFila> Turnos { get; } = [];
    public ObservableCollection<FacturaHistorialFila> Facturas { get; } = [];
    public ObservableCollection<ProcedimientoHistorialFila> Procedimientos { get; } = [];

    [ObservableProperty] private long _pacienteId;
    [ObservableProperty] private bool _ocupado;

    // ---------- Datos del paciente ----------
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _cedulaTexto = "—";
    [ObservableProperty] private string _telefonoTexto = "—";
    [ObservableProperty] private string _emailTexto = "—";
    [ObservableProperty] private string _nacimientoTexto = "—";
    [ObservableProperty] private string _edadTexto = "—";
    [ObservableProperty] private string _sexoTexto = "—";
    [ObservableProperty] private string _direccionTexto = "—";
    [ObservableProperty] private string _referidorTexto = "—";
    [ObservableProperty] private string _notasTexto = string.Empty;
    [ObservableProperty] private string _registradoTexto = string.Empty;

    /// <summary>Aviso visible cuando no tiene correo: sin correo no hay recordatorio.</summary>
    [ObservableProperty] private bool _sinEmail;

    // ---------- Resumen ----------
    [ObservableProperty] private string _totalFacturadoTexto = "0.00";
    [ObservableProperty] private string _totalPagadoTexto = "0.00";
    [ObservableProperty] private string _totalArsTexto = "0.00";
    [ObservableProperty] private string _ultimaVisitaTexto = "Todavía no ha venido";
    /// <summary>
    /// De dónde salió la fecha de arriba. Importa: una fecha cargada a mano al
    /// migrar el paciente no vale lo mismo que una cita atendida, y sin decirlo
    /// parecería que el sistema la registró.
    /// </summary>
    [ObservableProperty] private string _origenUltimaVisitaTexto = string.Empty;
    [ObservableProperty] private string _resumenCitasTexto = string.Empty;
    [ObservableProperty] private bool _mostrarArs;

    [ObservableProperty] private bool _sinCitas;
    [ObservableProperty] private bool _sinTurnos;
    [ObservableProperty] private bool _sinFacturas;
    [ObservableProperty] private bool _sinProcedimientos;

    public bool PuedeEditar => SesionActual.TienePermiso("clientes_editar");

    /// <summary>Sin permiso de agenda el botón no aparece: llevaría a un error.</summary>
    public bool PuedeAgendar => SesionActual.TienePermiso("citas");

    public async Task CargarAsync(long clienteId)
    {
        try
        {
            Ocupado = true;
            PacienteId = clienteId;
            OnPropertyChanged(nameof(PuedeEditar));
            OnPropertyChanged(nameof(PuedeAgendar));

            var h = await _pacientes.ObtenerHistorialAsync(clienteId);
            var p = h.Paciente;

            Nombre = p.Nombre;
            CedulaTexto = Vacio(p.Cedula);
            TelefonoTexto = Vacio(p.Telefono);
            EmailTexto = Vacio(p.Email);
            SinEmail = string.IsNullOrWhiteSpace(p.Email);
            NacimientoTexto = p.FechaNacimiento is { } n
                ? n.ToString("dd/MM/yyyy", CulturaRd)
                : "—";
            EdadTexto = EdadPaciente.Texto(p.FechaNacimiento);
            SexoTexto = p.Sexo switch
            {
                SexoPaciente.Femenino => "Femenino",
                SexoPaciente.Masculino => "Masculino",
                SexoPaciente.Otro => "Otro",
                _ => "—"
            };
            DireccionTexto = Vacio(p.Direccion);
            ReferidorTexto = Vacio(p.ReferidorNombre);
            NotasTexto = p.Notas ?? string.Empty;
            RegistradoTexto = "Registrado el " +
                FechaNegocio.AUtcLocal(p.CreatedAtUtc).ToString("dd/MM/yyyy", CulturaRd);

            Citas.Clear();
            foreach (var c in h.Citas)
                Citas.Add(new CitaHistorialFila(c));

            Turnos.Clear();
            foreach (var t in h.Turnos)
                Turnos.Add(new TurnoHistorialFila(t, _turnos.Etiqueta(t)));

            Facturas.Clear();
            foreach (var f in h.Facturas)
                Facturas.Add(new FacturaHistorialFila(f));

            Procedimientos.Clear();
            foreach (var pr in h.Procedimientos)
                Procedimientos.Add(new ProcedimientoHistorialFila(pr));

            SinCitas = Citas.Count == 0;
            SinTurnos = Turnos.Count == 0;
            SinFacturas = Facturas.Count == 0;
            SinProcedimientos = Procedimientos.Count == 0;

            TotalFacturadoTexto = h.TotalFacturado.ToString("N2", CulturaRd);
            TotalPagadoTexto = h.TotalPagadoPorElPaciente.ToString("N2", CulturaRd);
            TotalArsTexto = h.TotalCubiertoPorArs.ToString("N2", CulturaRd);
            // La fila de la ARS solo aparece si alguna vez le cubrieron algo:
            // en una clínica que trabaja privado sería un cero permanente.
            MostrarArs = h.TotalCubiertoPorArs > 0m;

            UltimaVisitaTexto = h.UltimaVisitaUtc is { } ultima
                ? FechaNegocio.AUtcLocal(ultima).ToString("dd/MM/yyyy", CulturaRd)
                : "Todavía no ha venido";

            // Si la que ganó es la cargada a mano, se dice. Se compara por DÍA
            // porque la manual no tiene hora y las otras sí.
            var manual = p.UltimaVisitaPrevia;
            OrigenUltimaVisitaTexto =
                manual is { } m && h.UltimaVisitaUtc is { } gano &&
                DateOnly.FromDateTime(FechaNegocio.AUtcLocal(gano)) == m
                    ? "Cargada a mano (antes del sistema)"
                    : string.Empty;

            ResumenCitasTexto = h.Citas.Count == 0
                ? "Sin citas"
                : $"{h.Citas.Count} cita(s) · {h.CitasAtendidas} atendida(s)" +
                  (h.CitasPerdidas > 0 ? $" · {h.CitasPerdidas} sin asistir" : string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            _dialogos.MostrarError("Ficha del paciente", ex.Message);
            Cerrado?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la ficha del paciente {Id}", clienteId);
            _dialogos.MostrarError("Ficha del paciente",
                $"No se pudo cargar la ficha.\n\n{ex.Message}");
            Cerrado?.Invoke();
        }
        finally
        {
            Ocupado = false;
        }
    }

    private static string Vacio(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? "—" : texto;

    [RelayCommand]
    private void Volver() => Cerrado?.Invoke();

    [RelayCommand]
    private void Editar() => EdicionSolicitada?.Invoke(PacienteId);

    /// <summary>
    /// Agendar sin salir a buscar al paciente de nuevo (pedido de la clínica
    /// 2026-08-27: la cita también se pone "Desde Paciente").
    /// </summary>
    [RelayCommand]
    private void AgendarCita() => CitaSolicitada?.Invoke(PacienteId);

    [RelayCommand]
    private Task RecargarAsync() => CargarAsync(PacienteId);
}
