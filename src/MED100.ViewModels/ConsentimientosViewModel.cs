using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// Los textos de consentimiento informado (015). Pedido de la clínica del
/// 2026-09-21: un botón que liste los documentos de consentimiento por
/// procedimiento, para firmar antes de cada procedimiento.
///
/// La misma pantalla sirve para las dos cosas que se hacen con esos textos:
///  * ESCRIBIRLOS y corregirlos (desde Procedimientos, con permiso).
///  * IMPRIMIR uno para un paciente (desde su ficha).
/// Son dos pantallas en una porque es la misma lista: separarlas obligaría a
/// mantener dos veces el mismo listado y a elegir el texto en un lado para
/// imprimirlo en el otro.
///
/// Lo firmado NO vive acá: el paciente firma en papel y el papel escaneado va a
/// su expediente (decisión de Yuber, 2026-09-24).
/// </summary>
public partial class ConsentimientosViewModel : ObservableObject
{
    private readonly ConsentimientoService _consentimientos;
    private readonly ProcedimientoService _procedimientos;
    private readonly MedicoService _medicos;
    private readonly IDialogService _dialogos;

    public ConsentimientosViewModel(ConsentimientoService consentimientos,
        ProcedimientoService procedimientos, MedicoService medicos, IDialogService dialogos)
    {
        _consentimientos = consentimientos;
        _procedimientos = procedimientos;
        _medicos = medicos;
        _dialogos = dialogos;
    }

    public ObservableCollection<Consentimiento> Lista { get; } = [];

    /// <summary>A qué procedimiento aplica una plantilla. El primero es "General".</summary>
    public ObservableCollection<Opcion<long?>> Procedimientos { get; } = [];

    /// <summary>Médico que va a firmar, si se sabe. Opcional: la línea va en blanco igual.</summary>
    public ObservableCollection<Opcion<long?>> Medicos { get; } = [];

    /// <summary>La hoja armada para este paciente. La imprime App (vista previa).</summary>
    public event Action<ConsentimientoImpreso>? ImpresionSolicitada;

    [ObservableProperty] private Consentimiento? _seleccionado;
    [ObservableProperty] private bool _ocupado;

    // ---- Formulario ----
    [ObservableProperty] private bool _editando;
    [ObservableProperty] private string _titulo = string.Empty;
    [ObservableProperty] private string _cuerpo = string.Empty;
    [ObservableProperty] private Opcion<long?>? _procedimientoDelFormulario;
    [ObservableProperty] private bool _activo = true;
    [ObservableProperty] private Opcion<long?>? _medicoSeleccionado;

    private long? _editandoId;

    /// <summary>El paciente para el que se va a imprimir, o null si solo se están editando textos.</summary>
    public Cliente? Paciente { get; private set; }

    public bool HayPaciente => Paciente is not null;
    public bool PuedeEditar => _consentimientos.PuedeEditar;
    public bool HaySeleccion => Seleccionado is not null;
    public bool PuedeImprimir => HayPaciente && HaySeleccion;

    public string Encabezado => Paciente is null
        ? "Consentimientos informados"
        : $"Consentimiento para {Paciente.Nombre}";

    public string Explicacion => Paciente is null
        ? "Son los textos que el paciente firma antes de un procedimiento. Se imprimen desde la ficha del paciente."
        : "Elegí el documento, imprimilo y que el paciente lo firme. El papel firmado se escanea a su expediente.";

    partial void OnSeleccionadoChanged(Consentimiento? value)
    {
        OnPropertyChanged(nameof(HaySeleccion));
        OnPropertyChanged(nameof(PuedeImprimir));
    }

    /// <summary>
    /// Carga la lista. Con <paramref name="paciente"/> la pantalla queda en modo
    /// impresión; sin él, en modo catálogo.
    /// </summary>
    public async Task CargarAsync(Cliente? paciente = null, long? procedimientoId = null)
    {
        Paciente = paciente;
        OnPropertyChanged(nameof(Paciente));
        OnPropertyChanged(nameof(HayPaciente));
        OnPropertyChanged(nameof(Encabezado));
        OnPropertyChanged(nameof(Explicacion));
        OnPropertyChanged(nameof(PuedeEditar));
        OnPropertyChanged(nameof(PuedeImprimir));

        try
        {
            Ocupado = true;

            Procedimientos.Clear();
            Procedimientos.Add(new Opcion<long?>(null, "General · cualquier procedimiento"));
            foreach (var p in await _procedimientos.ObtenerActivosAsync())
                Procedimientos.Add(new Opcion<long?>(p.Id, p.Nombre));

            Medicos.Clear();
            Medicos.Add(new Opcion<long?>(null, "Sin médico asignado"));
            foreach (var m in await _medicos.ObtenerActivosAsync())
                Medicos.Add(new Opcion<long?>(m.Id, m.Nombre));
            MedicoSeleccionado = Medicos[0];

            await RecargarListaAsync(procedimientoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los consentimientos");
            _dialogos.MostrarError("Consentimientos",
                $"No se pudo cargar la lista.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async Task RecargarListaAsync(long? procedimientoId = null)
    {
        var previo = Seleccionado?.Id;

        // Imprimiendo se muestran solo los activos: un texto desactivado es uno
        // que la clínica dejó de usar y no se puede poner a firmar.
        var lista = procedimientoId is not null
            ? await _consentimientos.ObtenerParaProcedimientoAsync(procedimientoId)
            : await _consentimientos.ObtenerTodosAsync(soloActivos: HayPaciente);

        Lista.Clear();
        foreach (var c in lista)
            Lista.Add(c);

        Seleccionado = Lista.FirstOrDefault(c => c.Id == previo) ?? Lista.FirstOrDefault();
    }

    // ---------------- Catálogo ----------------

    [RelayCommand]
    private void Nuevo()
    {
        _editandoId = null;
        Titulo = string.Empty;
        Cuerpo = string.Empty;
        ProcedimientoDelFormulario = Procedimientos.FirstOrDefault();
        Activo = true;
        Editando = true;
    }

    [RelayCommand]
    private void Editar()
    {
        if (Seleccionado is not { } actual)
            return;

        _editandoId = actual.Id;
        Titulo = actual.Titulo;
        Cuerpo = actual.Cuerpo;
        ProcedimientoDelFormulario = Procedimientos.FirstOrDefault(p => p.Valor == actual.ProcedimientoId)
                                     ?? Procedimientos.FirstOrDefault();
        Activo = actual.Activo;
        Editando = true;
    }

    [RelayCommand]
    private void CancelarEdicion()
    {
        Editando = false;
        _editandoId = null;
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        var datos = new ConsentimientoDatos(ProcedimientoDelFormulario?.Valor, Titulo, Cuerpo, Activo);

        try
        {
            Ocupado = true;
            if (_editandoId is { } id)
                await _consentimientos.ActualizarAsync(id, datos);
            else
                _editandoId = await _consentimientos.CrearAsync(datos);

            Editando = false;
            var guardado = _editandoId;
            _editandoId = null;
            await RecargarListaAsync();
            Seleccionado = Lista.FirstOrDefault(c => c.Id == guardado) ?? Seleccionado;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException
                                      or InvalidOperationException)
        {
            _dialogos.MostrarError("Consentimientos", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando un consentimiento");
            _dialogos.MostrarError("Consentimientos", $"No se pudo guardar.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task EliminarAsync()
    {
        if (Seleccionado is not { } actual)
            return;

        if (!_dialogos.Confirmar("Dar de baja",
                $"¿Dar de baja «{actual.Titulo}»?\n\n" +
                "Deja de aparecer para imprimir. Los papeles ya firmados con este texto " +
                "no se tocan: siguen en el expediente de cada paciente."))
            return;

        try
        {
            Ocupado = true;
            await _consentimientos.EliminarAsync(actual.Id);
            await RecargarListaAsync();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            _dialogos.MostrarError("Consentimientos", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    // ---------------- Imprimir ----------------

    [RelayCommand]
    private async Task ImprimirAsync()
    {
        if (Seleccionado is not { } plantilla || Paciente is not { } paciente)
            return;

        try
        {
            Ocupado = true;
            var medico = MedicoSeleccionado?.Valor is null ? null : MedicoSeleccionado.Etiqueta;
            var hoja = await _consentimientos.PrepararImpresionAsync(plantilla.Id, paciente, medico);
            ImpresionSolicitada?.Invoke(hoja);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Consentimientos", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error preparando el consentimiento {Id}", plantilla.Id);
            _dialogos.MostrarError("Consentimientos",
                $"No se pudo preparar el documento.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }
}
