using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Un documento del expediente, listo para la lista o la cuadrícula.</summary>
public record DocumentoFila(DocumentoPaciente Documento)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public long Id => Documento.Id;
    public string Nombre => Documento.Nombre;
    public string Familia => Documento.Familia;
    public string TipoTexto => ExpedienteService.EtiquetaTipo(Documento.Tipo);
    public string TamanoTexto => Documento.TamanoTexto;

    public string FechaTexto =>
        FechaNegocio.AUtcLocal(Documento.CreatedAtUtc).ToString("dd/MM/yyyy", CulturaRd);

    public string SubidoPorTexto =>
        string.IsNullOrWhiteSpace(Documento.SubidoPor) ? "—" : Documento.SubidoPor!;

    /// <summary>Glifo de Segoe MDL2 según la familia (vista de cuadrícula).</summary>
    public string Icono => Familia switch
    {
        "Imagen" => "",
        "Word" => "",
        "Excel" => "",
        "PDF" => "",
        "Comprimido" => "",
        "Texto" => "",
        _ => ""
    };
}

/// <summary>Paciente al que se puede re-ubicar un documento.</summary>
public record DestinoDocumento(long ClienteId, string Texto);

/// <summary>Opción del combo de tipo de documento.</summary>
public record OpcionTipoDocumento(TipoDocumentoPaciente Valor, string Etiqueta);

/// <summary>
/// El expediente de UN paciente: sus papeles, con su ficha arriba y un atajo a
/// los detalles completos.
///
/// Es una PÁGINA, no una ventana: se navega hacia ella y se vuelve con el botón
/// de atrás, como el resto de la aplicación.
///
/// ⚠️ Ley 172-13: lo que se guarda acá son datos de salud, sensibles. Cada
/// alta, apertura y borrado queda en auditoría, y eliminar es solo del Admin.
/// </summary>
public partial class ExpedientePacienteViewModel : ObservableObject
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly ExpedienteService _expedientes;
    private readonly ClienteService _pacientes;
    private readonly IDialogService _dialogos;

    /// <summary>Vuelve al almacén.</summary>
    public event Action? VolverSolicitado;

    /// <summary>Atajo a la ficha completa del paciente (citas, facturas, etc.).</summary>
    public event Action<long>? FichaSolicitada;

    public ExpedientePacienteViewModel(ExpedienteService expedientes, ClienteService pacientes,
        IDialogService dialogos)
    {
        _expedientes = expedientes;
        _pacientes = pacientes;
        _dialogos = dialogos;
    }

    public ObservableCollection<DocumentoFila> Documentos { get; } = [];

    /// <summary>Otros pacientes, para "Re-ubicar" cuando el papel era de otro.</summary>
    public ObservableCollection<DestinoDocumento> Destinos { get; } = [];

    public IReadOnlyList<OpcionTipoDocumento> TiposDocumento { get; } =
        Enum.GetValues<TipoDocumentoPaciente>()
            .Select(t => new OpcionTipoDocumento(t, ExpedienteService.EtiquetaTipo(t)))
            .ToList();

    [ObservableProperty] private long _clienteId;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _cedulaTexto = "—";
    [ObservableProperty] private string _telefonoTexto = "—";
    [ObservableProperty] private string _edadTexto = "—";
    [ObservableProperty] private string _referidorTexto = "—";

    [ObservableProperty] private bool _vistaLista = true;
    [ObservableProperty] private bool _sinDocumentos = true;
    [ObservableProperty] private string _resumenTexto = string.Empty;
    [ObservableProperty] private string _mensaje = string.Empty;
    [ObservableProperty] private bool _esError;
    [ObservableProperty] private bool _ocupado;

    /// <summary>Con qué tipo entran los archivos que se suban ahora.</summary>
    [ObservableProperty] private OpcionTipoDocumento? _tipoParaSubir;

    public bool VistaCuadricula => !VistaLista;
    partial void OnVistaListaChanged(bool value) => OnPropertyChanged(nameof(VistaCuadricula));

    /// <summary>Eliminar y re-ubicar son exclusivos del Admin.</summary>
    public bool PuedeAdministrar => SesionActual.TienePermiso("usuarios");

    /// <summary>
    /// Filtro del selector de archivos. Se arma acá y no en la View para que
    /// las extensiones permitidas tengan una sola fuente de verdad: la del
    /// servicio, que es quien las hace cumplir.
    /// </summary>
    public static string FiltroArchivos =>
        "Documentos e imágenes|" +
        string.Join(";", ExpedienteService.ExtensionesPermitidas.Select(x => "*" + x)) +
        "|Todos los archivos|*.*";

    public async Task CargarAsync(long clienteId)
    {
        ClienteId = clienteId;
        Mensaje = string.Empty;
        EsError = false;
        TipoParaSubir ??= TiposDocumento.First(t => t.Valor == TipoDocumentoPaciente.Otro);

        try
        {
            Ocupado = true;

            var paciente = await _pacientes.ObtenerPorIdAsync(clienteId)
                ?? throw new InvalidOperationException("El paciente no existe o fue eliminado.");

            Nombre = paciente.Nombre;
            CedulaTexto = Vacio(paciente.Cedula);
            TelefonoTexto = Vacio(paciente.Telefono);
            EdadTexto = EdadPaciente.Texto(paciente.FechaNacimiento);
            ReferidorTexto = Vacio(paciente.ReferidorNombre);

            await RecargarDocumentosAsync();
            OnPropertyChanged(nameof(PuedeAdministrar));
        }
        catch (UnauthorizedAccessException ex)
        {
            EsError = true;
            Mensaje = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el expediente del paciente {Id}", clienteId);
            EsError = true;
            Mensaje = $"No se pudo cargar el expediente.\n{ex.Message}";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async Task RecargarDocumentosAsync()
    {
        var documentos = await _expedientes.ObtenerAsync(ClienteId);
        Documentos.Clear();
        foreach (var documento in documentos)
            Documentos.Add(new DocumentoFila(documento));

        SinDocumentos = Documentos.Count == 0;
        ResumenTexto = Documentos.Count switch
        {
            0 => "Todavía no hay documentos guardados.",
            1 => "1 documento guardado",
            _ => $"{Documentos.Count} documentos guardados"
        };
    }

    [RelayCommand]
    private void VerEnLista() => VistaLista = true;

    [RelayCommand]
    private void VerEnCuadricula() => VistaLista = false;

    [RelayCommand]
    private void Volver() => VolverSolicitado?.Invoke();

    [RelayCommand]
    private void VerFicha() => FichaSolicitada?.Invoke(ClienteId);

    /// <summary>
    /// Sube uno o varios archivos de una vez. Si alguno falla —extensión no
    /// permitida, demasiado grande— los demás igual entran y al final se avisa
    /// cuáles quedaron afuera: descartar los seis porque el séptimo era un .exe
    /// obligaría a repetir todo.
    /// </summary>
    public async Task AgregarArchivosAsync(IEnumerable<string> rutas)
    {
        var rechazados = new List<string>();
        var agregados = 0;
        var tipo = TipoParaSubir?.Valor ?? TipoDocumentoPaciente.Otro;

        try
        {
            Ocupado = true;
            foreach (var ruta in rutas)
            {
                try
                {
                    await _expedientes.AgregarAsync(ClienteId, ruta, tipo);
                    agregados++;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _dialogos.MostrarError("Expediente", ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    // Cualquier cosa —la base, el disco— se informa y se sigue
                    // con el resto. Dejar escapar la excepción de un async void
                    // cerraría la aplicación sin decir una palabra.
                    Log.Error(ex, "Error subiendo {Archivo} al expediente", ruta);
                    rechazados.Add($"• {Path.GetFileName(ruta)}: {ex.Message.Split('\n')[0]}");
                }
            }
        }
        finally
        {
            Ocupado = false;
        }

        await CargarAsync(ClienteId);

        if (rechazados.Count > 0)
        {
            _dialogos.MostrarError("Algunos archivos no se pudieron guardar",
                $"Se guardaron {agregados}. No entraron:\n\n{string.Join("\n", rechazados)}");
        }
        else
        {
            Mensaje = agregados == 1 ? "Documento guardado." : $"{agregados} documentos guardados.";
            EsError = false;
        }
    }

    /// <summary>Descarga TODO el expediente en un ZIP (la View elige dónde).</summary>
    public async Task ExportarZipAsync(string rutaZip)
    {
        try
        {
            Ocupado = true;
            var cantidad = await _expedientes.ExportarZipAsync(ClienteId, rutaZip);
            _dialogos.Informar("Expediente exportado",
                $"Se guardaron {cantidad} documento(s) en:\n{rutaZip}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Exportar expediente", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exportando el expediente del paciente {Id}", ClienteId);
            _dialogos.MostrarError("Exportar expediente", $"No se pudo crear el ZIP.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Ruta real del archivo para que la View lo abra con su app de Windows.
    /// Devuelve null (y avisa) si el archivo ya no está en el disco.
    /// </summary>
    public async Task<string?> RutaParaAbrirAsync(DocumentoFila? fila)
    {
        if (fila is null)
            return null;
        try
        {
            return await _expedientes.RutaParaAbrirAsync(fila.Documento);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Abrir documento", ex.Message);
            return null;
        }
    }

    /// <summary>Guarda una copia donde el usuario eligió.</summary>
    public async Task GuardarCopiaAsync(DocumentoFila fila, string rutaDestino)
    {
        try
        {
            await _expedientes.GuardarCopiaAsync(fila.Documento, rutaDestino);
            _dialogos.Informar("Copia guardada", $"El documento se copió en:\n{rutaDestino}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _dialogos.MostrarError("Guardar documento", ex.Message);
        }
    }

    /// <summary>Reclasifica el papel sin volver a subirlo.</summary>
    public async Task<bool> CambiarTipoAsync(DocumentoFila fila, OpcionTipoDocumento tipo)
    {
        try
        {
            await _expedientes.CambiarTipoAsync(fila.Id, tipo.Valor);
            await CargarAsync(ClienteId);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Cambiar el tipo", ex.Message);
            return false;
        }
    }

    /// <summary>ELIMINAR — solo Admin.</summary>
    [RelayCommand]
    private async Task EliminarAsync(DocumentoFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar documento",
                $"¿Quitar '{fila.Nombre}' del expediente de {Nombre}?\n\n" +
                "Deja de aparecer en la lista y en el ZIP. Queda registrado quién lo quitó."))
            return;

        try
        {
            await _expedientes.EliminarAsync(fila.Id);
            await CargarAsync(ClienteId);
            Mensaje = "Documento eliminado del expediente.";
            EsError = false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Eliminar documento", ex.Message);
        }
    }

    /// <summary>Carga los pacientes a los que se puede re-ubicar (solo Admin).</summary>
    public async Task CargarDestinosAsync()
    {
        try
        {
            Destinos.Clear();
            foreach (var paciente in await _pacientes.ObtenerTodosAsync())
            {
                if (paciente.Id == ClienteId)
                    continue;   // el actual no es destino
                var cedula = string.IsNullOrWhiteSpace(paciente.Cedula) ? "sin cédula" : paciente.Cedula!;
                Destinos.Add(new DestinoDocumento(paciente.Id, $"{paciente.Nombre} · {cedula}"));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los pacientes destino para re-ubicar");
        }
    }

    /// <summary>RE-UBICAR — solo Admin: el papel era de otro paciente.</summary>
    public async Task<bool> ReubicarAsync(DocumentoFila fila, DestinoDocumento destino)
    {
        try
        {
            await _expedientes.MoverAsync(fila.Id, destino.ClienteId);
            await CargarAsync(ClienteId);
            _dialogos.Informar("Documento re-ubicado",
                $"'{fila.Nombre}' pasó al expediente de:\n{destino.Texto}");
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Re-ubicar documento", ex.Message);
            return false;
        }
    }

    private static string Vacio(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? "—" : texto;
}
