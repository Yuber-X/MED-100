using System.Windows;
using System.Windows.Input;
using MED100.Models;
using MED100.Services;
using Microsoft.Win32;

namespace MED100.Views;

/// <summary>
/// Leer la cédula del paciente sin tipearla (pedido de la clínica 2026-09-21;
/// Yuber pidió el 2026-09-24 los dos caminos: lector USB y cámara).
///
/// LOS TRES CAMINOS SON EL MISMO: cualquiera de ellos entrega un texto, y ese
/// texto lo interpreta <see cref="LectorCedula"/> —que vive en Services y tiene
/// tests—. Acá no se decide qué significa lo leído.
///
/// NADA se copia a la ficha sin que la persona lo vea: la ventana muestra lo
/// que entendió y recién con "Usar estos datos" se devuelve. Es a propósito —el
/// formato del código de la cédula dominicana no está documentado y lo que la
/// máquina llena sola, nadie lo revisa.
///
/// Code-behind de solo UI.
/// </summary>
public partial class EscanearCedulaWindow : Window
{
    private LectorCodigoCamara? _camara;

    public EscanearCedulaWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CajaLector.Focus();
        Closed += (_, _) => ApagarCamara();
    }

    /// <summary>Lo que se leyó y la persona aceptó. Null si canceló.</summary>
    public CedulaEscaneada? Resultado { get; private set; }

    private CedulaEscaneada? _leido;

    // ---------------- Lector USB ----------------

    private void CajaLector_KeyDown(object sender, KeyEventArgs e)
    {
        // Casi todos los lectores USB mandan Enter al final. El botón está
        // igual, para el que no lo mande.
        if (e.Key is Key.Enter or Key.Return)
        {
            Interpretar(CajaLector.Text);
            e.Handled = true;
        }
    }

    private void BotonLeer_Click(object sender, RoutedEventArgs e) => Interpretar(CajaLector.Text);

    // ---------------- Cámara ----------------

    private async void BotonCamara_Click(object sender, RoutedEventArgs e)
    {
        if (_camara is not null)
        {
            ApagarCamara();
            BotonCamara.Content = "Encender la cámara";
            AvisoCamara.Text = "La cámara está apagada.";
            AvisoCamara.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            AvisoCamara.Text = "Encendiendo la cámara…";
            _camara = new LectorCodigoCamara(Dispatcher);
            _camara.CuadroListo += imagen =>
            {
                Vista.Source = imagen;
                AvisoCamara.Visibility = Visibility.Collapsed;
            };
            _camara.CodigoLeido += texto =>
            {
                Interpretar(texto);
                ApagarCamara();
                BotonCamara.Content = "Encender la cámara";
            };

            await _camara.IniciarAsync();
            BotonCamara.Content = "Apagar la cámara";
        }
        catch (Exception ex)
        {
            ApagarCamara();
            BotonCamara.Content = "Encender la cámara";
            AvisoCamara.Visibility = Visibility.Visible;
            AvisoCamara.Text = ex.Message;
            Serilog.Log.Warning(ex, "No se pudo usar la cámara para leer la cédula");
        }
    }

    private void ApagarCamara()
    {
        _camara?.Dispose();
        _camara = null;
    }

    // ---------------- Foto ----------------

    private void BotonFoto_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Elegí la foto de la cédula",
            Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp|Todos los archivos|*.*"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            var texto = LectorCodigoCamara.DecodificarArchivo(dialogo.FileName);
            if (texto is null)
            {
                MessageBox.Show(this,
                    "No se encontró ningún código en esa imagen.\n\n" +
                    "Probá con una foto del REVERSO de la cédula, derecha y con buena luz.",
                    "Escanear cédula", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Interpretar(texto);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo leer la foto de la cédula");
            MessageBox.Show(this, $"No se pudo leer esa imagen.\n\n{ex.Message}",
                "Escanear cédula", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Resultado ----------------

    private void Interpretar(string? crudo)
    {
        _leido = LectorCedula.Interpretar(crudo);
        CajaLector.Text = _leido.Crudo;

        TextoCedula.Text = _leido.Cedula ?? "No se reconoció una cédula";
        TextoNombre.Text = _leido.Nombre is null ? string.Empty : $"Nombre: {_leido.Nombre}";

        var otros = new List<string>();
        if (_leido.FechaNacimiento is { } fecha)
            otros.Add($"Nacimiento: {fecha:dd/MM/yyyy}");
        if (_leido.Sexo is { } sexo)
            otros.Add($"Sexo: {Etiqueta(sexo)}");
        TextoOtros.Text = string.Join("   ·   ", otros);

        TextoCrudo.Text = string.IsNullOrWhiteSpace(_leido.Crudo)
            ? string.Empty
            : $"Leído: {_leido.Crudo}";

        BotonUsar.IsEnabled = _leido.HayAlgo;
    }

    private static string Etiqueta(SexoPaciente sexo) => sexo switch
    {
        SexoPaciente.Femenino => "Femenino",
        SexoPaciente.Masculino => "Masculino",
        _ => "Otro"
    };

    private void BotonUsar_Click(object sender, RoutedEventArgs e)
    {
        if (_leido is null || !_leido.HayAlgo)
            return;

        Resultado = _leido;
        DialogResult = true;
        Close();
    }

    private void BotonCancelar_Click(object sender, RoutedEventArgs e) => Close();
}
