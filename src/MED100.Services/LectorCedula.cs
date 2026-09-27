using System.Globalization;
using System.Text.RegularExpressions;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Lo que se pudo entender de una cédula escaneada. Lo que no se entendió
/// queda en <see cref="Crudo"/>: la pantalla lo muestra para que la persona
/// confirme antes de que nada toque la ficha del paciente.
/// </summary>
public record CedulaEscaneada(
    string? Cedula,
    string? Nombre,
    DateOnly? FechaNacimiento,
    SexoPaciente? Sexo,
    string Crudo)
{
    public bool HayAlgo => Cedula is not null || Nombre is not null
                           || FechaNacimiento is not null || Sexo is not null;
}

/// <summary>
/// Interpreta lo que devuelve un lector de cédula, venga del lector USB (que
/// "teclea" el contenido) o de la cámara (pedido de la clínica 2026-09-21;
/// Yuber pidió los dos caminos el 2026-09-24).
///
/// ⚠ EL FORMATO DEL CÓDIGO DE LA CÉDULA DOMINICANA NO ESTÁ DOCUMENTADO
/// PÚBLICAMENTE y varía según la emisión. Por eso esto NO parsea un formato
/// fijo: busca lo que puede reconocer sin lugar a dudas —los 11 dígitos, una
/// fecha, el sexo— y deja el resto en crudo. Es deliberadamente conservador:
/// inventar un nombre a partir de basura sería peor que no leer nada, porque
/// nadie revisa lo que la máquina "ya llenó".
///
/// Cuando se tenga el escaneo de una cédula real, acá es donde se agrega el
/// formato exacto (y los tests que lo fijen).
/// </summary>
public static class LectorCedula
{
    /// <summary>11 dígitos, con o sin guiones o espacios entre medio.</summary>
    private static readonly Regex OnceDigitos = new(
        @"(?<!\d)(\d[\s-]?){10}\d(?!\d)", RegexOptions.Compiled);

    private static readonly Regex FechaConSeparador = new(
        @"(?<!\d)(\d{2})[/\-.](\d{2})[/\-.](\d{4})(?!\d)", RegexOptions.Compiled);

    private static readonly Regex FechaCompacta = new(
        @"(?<!\d)(19|20)\d{6}(?!\d)", RegexOptions.Compiled);

    /// <summary>Nombre: solo si el contenido viene separado en campos.</summary>
    private static readonly Regex SoloLetras = new(
        @"^[A-ZÁÉÍÓÚÑÜ][A-ZÁÉÍÓÚÑÜa-záéíóúñü'´`]*(\s+[A-ZÁÉÍÓÚÑÜ][A-ZÁÉÍÓÚÑÜa-záéíóúñü'´`]*)+$",
        RegexOptions.Compiled);

    private static readonly char[] Separadores = ['|', '^', ';', '\t', '#'];

    public static CedulaEscaneada Interpretar(string? crudo)
    {
        var texto = (crudo ?? string.Empty).Trim();
        if (texto.Length == 0)
            return new CedulaEscaneada(null, null, null, null, string.Empty);

        return new CedulaEscaneada(
            BuscarCedula(texto),
            BuscarNombre(texto),
            BuscarFecha(texto),
            BuscarSexo(texto),
            texto);
    }

    /// <summary>
    /// Deja la cédula como se escribe en RD: 001-1234567-8. El lector puede
    /// entregarla pegada, con guiones o partida en pedazos.
    /// </summary>
    public static string? Formatear(string? cedula)
    {
        var digitos = new string((cedula ?? string.Empty).Where(char.IsDigit).ToArray());
        return digitos.Length == 11
            ? $"{digitos[..3]}-{digitos[3..10]}-{digitos[10..]}"
            : null;
    }

    private static string? BuscarCedula(string texto)
    {
        var coincidencia = OnceDigitos.Match(texto);
        return coincidencia.Success ? Formatear(coincidencia.Value) : null;
    }

    /// <summary>
    /// Solo cuando el contenido viene partido en campos (|, ^, ; o tabulador) y
    /// uno de ellos es claramente un nombre: dos o más palabras, solo letras.
    ///
    /// Un texto corrido NO se toca. Sacarle "el nombre" a una tira de
    /// caracteres es adivinar, y lo que se adivina acá termina impreso en una
    /// factura.
    /// </summary>
    private static string? BuscarNombre(string texto)
    {
        if (!texto.Any(Separadores.Contains))
            return null;

        var candidatos = texto
            .Split(Separadores, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(campo => campo.Length is >= 5 and <= 80)
            .Where(campo => SoloLetras.IsMatch(campo))
            .ToList();

        // Si hay dos campos que parecen nombre (nombres y apellidos separados),
        // se unen en el orden en que vinieron.
        return candidatos.Count switch
        {
            0 => null,
            1 => Normalizar(candidatos[0]),
            _ => Normalizar(string.Join(' ', candidatos.Take(2)))
        };
    }

    private static DateOnly? BuscarFecha(string texto)
    {
        var conSeparador = FechaConSeparador.Match(texto);
        if (conSeparador.Success &&
            DateOnly.TryParseExact(conSeparador.Value.Replace('.', '/').Replace('-', '/'),
                "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) &&
            EsCreible(fecha))
            return fecha;

        var compacta = FechaCompacta.Match(texto);
        if (compacta.Success &&
            DateOnly.TryParseExact(compacta.Value, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var otra) &&
            EsCreible(otra))
            return otra;

        return null;
    }

    /// <summary>
    /// Una fecha de nacimiento no está en el futuro ni hace 150 años. Sin este
    /// filtro, cualquier número de ocho cifras del código pasaría por fecha.
    /// </summary>
    private static bool EsCreible(DateOnly fecha)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        return fecha <= hoy && fecha >= hoy.AddYears(-150);
    }

    /// <summary>
    /// Sexo solo cuando viene como campo suelto ("M", "F", "MASCULINO"). Una
    /// "M" en medio de una palabra no cuenta.
    /// </summary>
    private static SexoPaciente? BuscarSexo(string texto)
    {
        var campos = texto
            .Split([.. Separadores, ' ', ',', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant());

        foreach (var campo in campos)
        {
            if (campo is "M" or "MASCULINO" or "HOMBRE")
                return SexoPaciente.Masculino;
            if (campo is "F" or "FEMENINO" or "MUJER")
                return SexoPaciente.Femenino;
        }
        return null;
    }

    /// <summary>NOMBRE APELLIDO → Nombre Apellido, que es como se ve en la ficha.</summary>
    private static string Normalizar(string nombre)
    {
        var palabras = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Length <= 3 && p.All(char.IsUpper) && p is "DE" or "DEL" or "LA" or "LOS"
                ? p.ToLowerInvariant()
                : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant());
        return string.Join(' ', palabras);
    }
}
