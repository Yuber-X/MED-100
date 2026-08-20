using System.Globalization;
using System.IO;

namespace MED100.Common;

/// <summary>
/// La segunda copia de "cuándo empezó el demo", fuera de la base de datos.
///
/// POR QUÉ EXISTE. El demo se cuenta desde la fecha guardada en la tabla
/// <c>licencia</c>. Si esa fuera la única copia, reiniciar los 15 días sería
/// tan fácil como borrar la base y volver a abrir — y durante el demo eso no
/// cuesta nada, porque todavía no hay datos que perder. Con esta ancla, borrar
/// la base no mueve la fecha: la app toma siempre la MÁS VIEJA de las dos.
///
/// Vive en <c>%ProgramData%\MED-100\</c> y no junto al .exe: desinstalar la
/// aplicación no la borra. Va cifrada con DPAPI de máquina, así que abrirla con
/// el Bloc de notas y correr la fecha no funciona — el archivo deja de
/// descifrar y se descarta (se vuelve a la fecha de la base, que es la otra
/// copia).
///
/// Si el archivo no está, no pasa nada malo: se vuelve a escribir con lo que
/// diga la base. Perderlo NO deja la app inservible, y borrarlo a mano
/// tampoco regala días.
/// </summary>
public class AnclaLicencia
{
    private const string Marca = "MED100v1";

    private readonly string _ruta;

    public AnclaLicencia()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MED-100", "licencia.dat"))
    {
    }

    /// <summary>Ruta explícita (tests).</summary>
    public AnclaLicencia(string ruta) => _ruta = ruta;

    public string Ruta => _ruta;

    /// <summary>
    /// Lo anclado, o null si no hay archivo, está corrupto o lo editaron.
    /// Nunca tira excepción: en el peor caso la app se guía por la base.
    /// </summary>
    public (DateTime InicioUtc, bool Activada)? Leer()
    {
        try
        {
            if (!File.Exists(_ruta))
                return null;

            var plano = Secreto.RevelarConElEquipo(File.ReadAllText(_ruta));
            var partes = plano.Split('|');
            if (partes.Length != 3 || partes[0] != Marca)
                return null;

            if (!long.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                return null;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return null;

            return (new DateTime(ticks, DateTimeKind.Utc), partes[2] == "1");
        }
        catch (Exception)
        {
            // Disco lleno, permisos, archivo a medio escribir: la base manda
            return null;
        }
    }

    /// <summary>
    /// Guarda el ancla. Devuelve false si no se pudo (otro usuario de Windows
    /// creó el archivo y esta cuenta no puede pisarlo, por ejemplo). El
    /// llamador NO debe tratarlo como error fatal: la base sigue siendo la
    /// fuente principal.
    /// </summary>
    public bool Escribir(DateTime inicioUtc, bool activada)
    {
        try
        {
            var carpeta = Path.GetDirectoryName(_ruta);
            if (!string.IsNullOrEmpty(carpeta))
                Directory.CreateDirectory(carpeta);

            var plano = string.Join('|',
                Marca,
                inicioUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                activada ? "1" : "0");

            File.WriteAllText(_ruta, Secreto.ProtegerConElEquipo(plano));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
