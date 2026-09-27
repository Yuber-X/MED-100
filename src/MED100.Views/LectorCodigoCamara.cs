using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using ZXing;
using ZXing.Common;

namespace MED100.Views;

/// <summary>
/// Lee el código de la cédula con la cámara de la computadora (pedido de la
/// clínica 2026-09-21; Yuber pidió lector USB Y cámara el 2026-09-24).
///
/// POR QUÉ VIVE EN Views Y NO EN Services: usa las API de cámara de Windows
/// (WinRT), y este es el único proyecto que apunta a
/// <c>net8.0-windows10.0.19041.0</c>. Lo que sí es lógica —entender lo que dice
/// el código leído— vive en <c>LectorCedula</c>, del lado de Services, y tiene
/// sus tests.
///
/// El decodificador acepta QR y PDF417: la cédula dominicana trae PDF417 en el
/// reverso y las emisiones nuevas agregan QR. Cuál es cuál no se decide acá, se
/// prueban los dos.
/// </summary>
public sealed class LectorCodigoCamara : IDisposable
{
    private readonly Dispatcher _despachador;
    private MediaCapture? _captura;
    private MediaFrameReader? _lector;
    private bool _procesando;
    private bool _detenido;

    public LectorCodigoCamara(Dispatcher despachador) => _despachador = despachador;

    /// <summary>Cada cuadro de la cámara, ya en formato de WPF, para la vista previa.</summary>
    public event Action<BitmapSource>? CuadroListo;

    /// <summary>Se leyó un código. Llega UNA sola vez: después el lector se detiene.</summary>
    public event Action<string>? CodigoLeido;

    /// <summary>
    /// Enciende la cámara. Tira con un mensaje entendible si no hay cámara o si
    /// Windows tiene bloqueado el acceso — que es el caso más común y no es un
    /// error del programa, sino un permiso del sistema.
    /// </summary>
    public async Task IniciarAsync()
    {
        var grupos = await MediaFrameSourceGroup.FindAllAsync();
        var elegido = grupos
            .Select(g => new
            {
                Grupo = g,
                Fuente = g.SourceInfos.FirstOrDefault(s =>
                    s.MediaStreamType == MediaStreamType.VideoRecord ||
                    s.MediaStreamType == MediaStreamType.VideoPreview)
            })
            .FirstOrDefault(x => x.Fuente is not null)
            ?? throw new InvalidOperationException(
                "No se encontró ninguna cámara en esta computadora. " +
                "Podés leer la cédula con el lector USB o desde una foto.");

        _captura = new MediaCapture();
        try
        {
            await _captura.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = elegido.Grupo,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            });
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Windows no está dejando usar la cámara. Activala en " +
                "Configuración → Privacidad y seguridad → Cámara, y volvé a intentar.");
        }

        var fuente = _captura.FrameSources[elegido.Fuente!.Id];
        _lector = await _captura.CreateFrameReaderAsync(fuente, MediaEncodingSubtypes.Bgra8);
        _lector.FrameArrived += CuadroRecibido;
        await _lector.StartAsync();
    }

    private void CuadroRecibido(MediaFrameReader lector, MediaFrameArrivedEventArgs e)
    {
        // Un cuadro a la vez: decodificar tarda más que el intervalo entre
        // cuadros y sin este candado se apilarían hasta comerse la memoria.
        if (_procesando || _detenido)
            return;

        _procesando = true;
        try
        {
            using var cuadro = lector.TryAcquireLatestFrame();
            var mapa = cuadro?.VideoMediaFrame?.SoftwareBitmap;
            if (mapa is null)
                return;

            using var bgra = mapa.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                             && mapa.BitmapAlphaMode == BitmapAlphaMode.Premultiplied
                ? null
                : SoftwareBitmap.Convert(mapa, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var usable = bgra ?? mapa;

            var ancho = usable.PixelWidth;
            var alto = usable.PixelHeight;
            var pixeles = new byte[ancho * alto * 4];
            usable.CopyToBuffer(pixeles.AsBuffer());

            Mostrar(pixeles, ancho, alto);

            if (Decodificar(pixeles, ancho, alto) is { } texto)
            {
                _detenido = true;
                _despachador.BeginInvoke(() => CodigoLeido?.Invoke(texto));
            }
        }
        catch (Exception ex)
        {
            // Un cuadro que falla no rompe la lectura: el siguiente puede salir
            // bien. Si la cámara se desconectó, el usuario lo ve porque la
            // imagen se congela.
            Serilog.Log.Debug(ex, "Cuadro de cámara descartado");
        }
        finally
        {
            _procesando = false;
        }
    }

    private void Mostrar(byte[] pixeles, int ancho, int alto)
    {
        _despachador.BeginInvoke(() =>
        {
            var imagen = BitmapSource.Create(ancho, alto, 96, 96, PixelFormats.Pbgra32, null,
                pixeles, ancho * 4);
            imagen.Freeze();
            CuadroListo?.Invoke(imagen);
        });
    }

    /// <summary>
    /// Busca un código en una imagen BGRA. Público porque el mismo camino sirve
    /// para leer una FOTO de la cédula, que es el plan B cuando la cámara no
    /// enfoca o la clínica no tiene uno conectado.
    /// </summary>
    public static string? Decodificar(byte[] bgra, int ancho, int alto)
    {
        var lector = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats =
                [
                    BarcodeFormat.QR_CODE,
                    BarcodeFormat.PDF_417,
                    BarcodeFormat.CODE_128,
                    BarcodeFormat.DATA_MATRIX
                ]
            }
        };

        var fuente = new RGBLuminanceSource(bgra, ancho, alto,
            RGBLuminanceSource.BitmapFormat.BGRA32);
        return lector.Decode(fuente)?.Text;
    }

    /// <summary>Lee el código de un archivo de imagen (una foto de la cédula).</summary>
    public static string? DecodificarArchivo(string ruta)
    {
        var original = new BitmapImage();
        original.BeginInit();
        original.CacheOption = BitmapCacheOption.OnLoad;
        original.UriSource = new Uri(ruta, UriKind.Absolute);
        original.EndInit();

        var convertida = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        var ancho = convertida.PixelWidth;
        var alto = convertida.PixelHeight;
        var pixeles = new byte[ancho * alto * 4];
        convertida.CopyPixels(pixeles, ancho * 4, 0);

        return Decodificar(pixeles, ancho, alto);
    }

    public void Dispose()
    {
        _detenido = true;
        try
        {
            if (_lector is not null)
            {
                _lector.FrameArrived -= CuadroRecibido;
                _lector.Dispose();
                _lector = null;
            }
            _captura?.Dispose();
            _captura = null;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error cerrando la cámara");
        }
    }
}
