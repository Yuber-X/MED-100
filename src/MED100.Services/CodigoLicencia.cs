using System.Security.Cryptography;
using System.Text;

namespace MED100.Services;

/// <summary>
/// La llave del producto: qué se considera un código escrito correctamente y
/// cuál de todos es el bueno.
///
/// FORMATO: <c>MED1-XXXXX-XXXXX-XXXXX</c>. El alfabeto no tiene 0, 1, I ni O
/// a propósito: la llave se dicta por teléfono y se copia de un papel, y esas
/// cuatro son justamente las que se confunden con O, l y 0.
///
/// ⚠️ LO QUE ESTO ES Y LO QUE NO ES. Es UNA llave igual para todos los
/// clientes (decisión de Yuber, 2026-08-18). Sirve para que la app no quede
/// abierta de par en par y para poner una fecha de compra, pero NO impide que
/// un cliente le pase la llave a otro: el día que eso pase, la única defensa
/// es publicar una versión nueva con otra llave. Por eso <see cref="Hashes"/>
/// es una LISTA: se le agrega la llave nueva y se retira la quemada, y las
/// instalaciones ya activadas no se enteran (la activación queda guardada en
/// la base, no se vuelve a pedir el código).
///
/// La llave NO viaja en el binario: solo viaja su SHA-256. Abrir el .exe con
/// un editor y buscar cadenas no la revela.
/// </summary>
public static class CodigoLicencia
{
    /// <summary>Sin 0/1/I/O: son las que se confunden al dictar la llave.</summary>
    private const string Alfabeto = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>
    /// Distingue este hash del de cualquier otra cosa que use SHA-256. Si
    /// mañana MED-100 hashea otra cosa, dos entradas iguales no colisionan.
    /// </summary>
    private const string Sal = "MED100.Licencia.v1|";

    /// <summary>
    /// SHA-256 de las llaves aceptadas. Se agregan y se retiran acá.
    /// · 1a — llave de venta, generada el 2026-08-18.
    /// </summary>
    private static readonly string[] Hashes =
    [
        "e45ae8847e4218b4165d0efbe61dbf0c1a5eae11432fbcb2c6134838a7a4f98f"
    ];

    /// <summary>Cuántos caracteres tiene la llave ya sin guiones.</summary>
    public const int Largo = 19;

    /// <summary>
    /// Deja el código como se compara: mayúsculas y sin nada que no sea del
    /// alfabeto. Así da igual que lo escriban con guiones, con espacios, en
    /// minúscula o pegado — que es como llega cuando lo copian de WhatsApp.
    /// </summary>
    public static string Normalizar(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return string.Empty;

        var limpio = new StringBuilder(codigo.Length);
        foreach (var c in codigo.ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                limpio.Append(c);
        }
        return limpio.ToString();
    }

    /// <summary>True si el código es una de las llaves válidas.</summary>
    public static bool EsValido(string? codigo)
    {
        var normalizado = Normalizar(codigo);
        if (normalizado.Length == 0)
            return false;

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Sal + normalizado))).ToLowerInvariant();

        // Comparación de tiempo constante. Acá no cambia nada práctico (el
        // atacante tiene el binario en la mano), pero es una línea y evita
        // que este código se copie mal a un lugar donde sí importe.
        var esperado = Encoding.ASCII.GetBytes(hash);
        var valido = false;
        foreach (var conocido in Hashes)
        {
            if (CryptographicOperations.FixedTimeEquals(esperado, Encoding.ASCII.GetBytes(conocido)))
                valido = true;
        }
        return valido;
    }

    /// <summary>
    /// Devuelve el código con los guiones puestos, para mostrarlo mientras se
    /// escribe: MED1ABCDE… → MED1-ABCDE-…
    ///
    /// El ejemplo es inventado a propósito. Acá había pegados los primeros
    /// caracteres de la llave de venta real: la llave es UNA sola para todos
    /// los clientes y no puede quedar ni a pedazos en el repositorio — de eso
    /// se trata que en el binario viaje solo su SHA-256.
    /// </summary>
    public static string Formatear(string? codigo)
    {
        var n = Normalizar(codigo);
        if (n.Length == 0)
            return string.Empty;

        // MED1 · 5 · 5 · 5
        var partes = new List<string>();
        var cortes = new[] { 4, 5, 5, 5 };
        var i = 0;
        foreach (var largo in cortes)
        {
            if (i >= n.Length)
                break;
            partes.Add(n.Substring(i, Math.Min(largo, n.Length - i)));
            i += largo;
        }
        if (i < n.Length)
            partes.Add(n[i..]);   // lo que sobre: se ve, para que el usuario note que escribió de más

        return string.Join('-', partes);
    }

    /// <summary>
    /// Solo para la herramienta que genera llaves nuevas: el hash que hay que
    /// pegar en <see cref="Hashes"/> para que una llave pase a ser válida.
    /// </summary>
    public static string HashDe(string codigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sal + Normalizar(codigo))))
            .ToLowerInvariant();

    /// <summary>El alfabeto, expuesto para el generador de llaves.</summary>
    public static string AlfabetoLlave => Alfabeto;
}
