using System.IO.Compression;
using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Expediente digital del paciente (pedido de Yuber 2026-08-14): guardar todo
/// lo que el paciente entrega —cédula, carné de la ARS, consentimientos,
/// referimientos, estudios que trajo— para que la próxima vez que venga ya
/// esté a mano.
///
/// Es el mismo diseño que ya probó FAControl con los contratos:
///  · el ARCHIVO va al disco, no a la base — un BLOB por cada foto de cédula
///    hincharía el dump hasta hacer inviable el respaldo diario;
///  · la base guarda su FICHA, con la ruta relativa a la carpeta raíz, así
///    mover la instalación de PC no rompe nada.
///
/// PERMISOS:
///  · ver, abrir y guardar copia → quien tenga el permiso 'expedientes';
///  · ELIMINAR y RE-UBICAR → solo Admin. Son los dos que rompen un expediente.
///
/// SEGURIDAD: solo entran las extensiones de la lista blanca. Nada de .exe,
/// .bat, .ps1 ni .lnk: el documento se abre con doble clic y un ejecutable
/// disfrazado de "cédula.pdf.exe" sería una puerta de entrada al equipo.
///
/// ⚠️ LEY 172-13: lo que se archiva acá son datos de salud, que son SENSIBLES.
/// Por eso cada alta, apertura y borrado queda en <c>auditoria</c> con nombre y
/// hora. Ojo con §1.1 del CLAUDE.md: MED-100 sigue SIN campos de diagnóstico ni
/// tratamiento — esto es un archivador de papeles escaneados, no un expediente
/// clínico estructurado.
/// </summary>
public class ExpedienteService
{
    /// <summary>Lo que tiene sentido guardar de un paciente, y nada más.</summary>
    public static readonly string[] ExtensionesPermitidas =
    [
        ".pdf",
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
        ".heic", ".heif",                       // fotos de iPhone
        ".doc", ".docx", ".rtf", ".odt",
        ".xls", ".xlsx", ".csv", ".ods",
        ".zip", ".rar", ".7z",
        ".txt"
    ];

    /// <summary>Tope por archivo. Una cédula escaneada no pesa más que esto.</summary>
    public const long MaxBytesPorArchivo = 50L * 1024 * 1024;

    private readonly DocumentoPacienteRepository _documentos;
    private readonly ClienteRepository _pacientes;
    private readonly AuditoriaService _auditoria;
    private readonly string _raiz;

    public ExpedienteService(DocumentoPacienteRepository documentos, ClienteRepository pacientes,
        AuditoriaService auditoria, AjustesLocales ajustes)
    {
        _documentos = documentos;
        _pacientes = pacientes;
        _auditoria = auditoria;
        _raiz = CarpetaRaiz(ajustes);
    }

    /// <summary>
    /// Carpeta donde viven los archivos. Configurable para poder apuntarla a
    /// una unidad con espacio; por defecto va junto al ejecutable.
    /// </summary>
    public static string CarpetaRaiz(AjustesLocales ajustes) =>
        string.IsNullOrWhiteSpace(ajustes.CarpetaExpedientes)
            ? Path.Combine(AppContext.BaseDirectory, "expedientes")
            : ajustes.CarpetaExpedientes;

    public string Raiz => _raiz;

    /// <summary>Carpeta relativa de un paciente dentro de la raíz.</summary>
    private static string CarpetaDe(long clienteId) => $"pacientes/{clienteId}";

    /// <summary>Ruta real del archivo en el disco.</summary>
    public string RutaAbsoluta(DocumentoPaciente documento) =>
        Path.Combine(_raiz, documento.RutaRelativa.Replace('/', Path.DirectorySeparatorChar));

    // ---------- Lectura ----------

    /// <summary>El tablero del módulo: todos los pacientes con su conteo.</summary>
    public Task<List<ResumenExpediente>> ObtenerResumenAsync(CancellationToken ct = default)
    {
        ExigirVer();
        return _documentos.ObtenerResumenAsync(ct);
    }

    public Task<List<DocumentoPaciente>> ObtenerAsync(long clienteId, CancellationToken ct = default)
    {
        ExigirVer();
        return _documentos.ObtenerDeAsync(clienteId, ct);
    }

    /// <summary>
    /// ¿El paciente ya tiene un documento que empiece así? Sirve para no
    /// archivar dos veces la misma factura: el nombre del PDF lleva el número
    /// de comprobante, que es único.
    /// </summary>
    public async Task<bool> YaTieneDocumentoAsync(long clienteId, string prefijoNombre,
        CancellationToken ct = default)
    {
        ExigirVer();
        var documentos = await _documentos.ObtenerDeAsync(clienteId, ct);
        return documentos.Any(d =>
            d.Nombre.StartsWith(prefijoNombre, StringComparison.CurrentCultureIgnoreCase));
    }

    // ---------- Alta ----------

    /// <summary>
    /// Copia el archivo al expediente. El original del usuario NO se mueve ni
    /// se borra: si el expediente se corrompe, su archivo sigue donde estaba.
    /// </summary>
    public async Task<DocumentoPaciente> AgregarAsync(long clienteId, string rutaOrigen,
        TipoDocumentoPaciente tipo = TipoDocumentoPaciente.Otro, string? notas = null,
        CancellationToken ct = default)
    {
        ExigirVer();

        var paciente = await _pacientes.ObtenerPorIdAsync(clienteId, ct)
            ?? throw new InvalidOperationException("El paciente no existe o fue eliminado.");

        if (!File.Exists(rutaOrigen))
            throw new InvalidOperationException($"No encuentro el archivo:\n{rutaOrigen}");

        var info = new FileInfo(rutaOrigen);
        var extension = info.Extension.ToLowerInvariant();
        ValidarExtension(extension);

        if (info.Length > MaxBytesPorArchivo)
            throw new InvalidOperationException(
                $"'{info.Name}' pesa {info.Length / (1024d * 1024d):0.#} MB y el máximo es " +
                $"{MaxBytesPorArchivo / (1024 * 1024)} MB. Comprimilo o escaneálo con menos resolución.");

        // 1. La ficha primero: su id da el nombre único del archivo en disco.
        var id = await _documentos.CrearAsync(clienteId, info.Name, "pendiente", extension,
            info.Length, tipo, notas, SesionActual.HaySesionActiva ? SesionActual.Id : null, ct);

        var relativa = $"{CarpetaDe(clienteId)}/{id}_{LimpiarNombre(info.Name)}";
        var destino = Path.Combine(_raiz, relativa.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        try
        {
            File.Copy(rutaOrigen, destino, overwrite: true);
        }
        catch
        {
            // Sin archivo no hay documento: se borra la ficha para no dejar
            // una fila apuntando a un archivo que nunca llegó a existir.
            await _documentos.EliminarAsync(id, ct);
            throw;
        }

        await _documentos.ActualizarRutaAsync(id, relativa, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.DocumentoPaciente, id,
            $"Documento '{info.Name}' agregado al expediente de {paciente.Nombre}", ct);
        Log.Information("Expediente del paciente {Paciente}: documento {Id} ({Nombre}) agregado",
            clienteId, id, info.Name);

        return await _documentos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El documento se guardó pero no se pudo releer.");
    }

    /// <summary>
    /// Archiva en el expediente un documento que la app ACABA DE GENERAR: el
    /// PDF de la factura al cobrar, el del turno al darlo (pedido 2026-08-15).
    ///
    /// <b>Por qué no pasa por <see cref="AgregarAsync"/>:</b> ese exige el
    /// permiso <c>expedientes</c>, y el Cajero no lo tiene — a propósito, no ve
    /// los papeles del paciente. Pero el Cajero ES quien emite las facturas: si
    /// el archivado pidiera ese permiso, ninguna factura cobrada en el mostrador
    /// quedaría guardada, y el fallo sería invisible.
    ///
    /// La distinción es real: el permiso <c>expedientes</c> gobierna
    /// <i>hojear los papeles de un paciente</i>. Esto otro es la app guardando
    /// copia de un documento que el usuario ya tuvo permiso de emitir. Igual
    /// exige sesión abierta y queda en auditoría con nombre y hora.
    ///
    /// No lanza: si falla, se registra y se sigue. La factura ya está emitida y
    /// el paciente está esperando su papel — no se le puede tirar un error
    /// encima por una copia que no pidió.
    /// </summary>
    /// <returns>True si quedó archivado.</returns>
    public async Task<bool> ArchivarImpresoAsync(long clienteId, string rutaArchivo,
        TipoDocumentoPaciente tipo, string? notas = null, CancellationToken ct = default)
    {
        if (!SesionActual.HaySesionActiva)
            return false;

        try
        {
            var paciente = await _pacientes.ObtenerPorIdAsync(clienteId, ct);
            if (paciente is null)
            {
                Log.Warning("No se archivó el documento: el paciente {Id} no existe", clienteId);
                return false;
            }

            var info = new FileInfo(rutaArchivo);
            if (!info.Exists)
            {
                Log.Warning("No se archivó el documento: falta el archivo {Ruta}", rutaArchivo);
                return false;
            }

            var id = await _documentos.CrearAsync(clienteId, info.Name, "pendiente",
                info.Extension.ToLowerInvariant(), info.Length, tipo, notas,
                SesionActual.Id, ct);

            var relativa = $"{CarpetaDe(clienteId)}/{id}_{LimpiarNombre(info.Name)}";
            var destino = Path.Combine(_raiz, relativa.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

            try
            {
                File.Copy(rutaArchivo, destino, overwrite: true);
            }
            catch
            {
                await _documentos.EliminarAsync(id, ct);
                throw;
            }

            await _documentos.ActualizarRutaAsync(id, relativa, ct);
            await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.DocumentoPaciente, id,
                $"Copia de '{info.Name}' archivada automáticamente en el expediente de {paciente.Nombre}",
                ct);
            Log.Information("Documento {Nombre} archivado en el expediente del paciente {Paciente}",
                info.Name, clienteId);
            return true;
        }
        catch (Exception ex)
        {
            // A propósito se traga TODO: el documento ya se emitió e imprimió,
            // que era lo que el usuario pidió. Perder la copia es molesto;
            // reventarle la pantalla en medio del cobro es peor.
            Log.Warning(ex, "No se pudo archivar el documento en el expediente del paciente {Id}",
                clienteId);
            return false;
        }
    }

    // ---------- Uso ----------

    /// <summary>
    /// Ruta lista para abrir con la app de Windows que corresponda. Vuelve a
    /// validar la extensión: aunque la base diga otra cosa, acá no se abre nada
    /// que no esté en la lista blanca.
    ///
    /// Queda en auditoría: son datos de salud y la Ley 172-13 obliga a poder
    /// decir quién los miró.
    /// </summary>
    public async Task<string> RutaParaAbrirAsync(DocumentoPaciente documento,
        CancellationToken ct = default)
    {
        ExigirVer();
        var ruta = RutaAbsoluta(documento);
        ValidarExtension(Path.GetExtension(ruta).ToLowerInvariant());
        if (!File.Exists(ruta))
            throw new InvalidOperationException(ArchivoPerdido(documento));

        await _auditoria.RegistrarAsync(AccionAuditoria.Consultar, DbNames.DocumentoPaciente,
            documento.Id, $"Documento '{documento.Nombre}' abierto", ct);
        return ruta;
    }

    /// <summary>Guarda una copia donde el usuario quiera.</summary>
    public async Task GuardarCopiaAsync(DocumentoPaciente documento, string rutaDestino,
        CancellationToken ct = default)
    {
        ExigirVer();
        var origen = RutaAbsoluta(documento);
        if (!File.Exists(origen))
            throw new InvalidOperationException(ArchivoPerdido(documento));

        File.Copy(origen, rutaDestino, overwrite: true);
        await _auditoria.RegistrarAsync(AccionAuditoria.Consultar, DbNames.DocumentoPaciente,
            documento.Id, $"Copia guardada de '{documento.Nombre}'", ct);
    }

    /// <summary>
    /// Baja TODO el expediente del paciente en un ZIP: para llevárselo, para
    /// mandárselo a otra clínica o para migrar.
    /// </summary>
    public async Task<int> ExportarZipAsync(long clienteId, string rutaZip,
        CancellationToken ct = default)
    {
        ExigirVer();

        var documentos = await _documentos.ObtenerDeAsync(clienteId, ct);
        if (documentos.Count == 0)
            throw new InvalidOperationException("Este paciente todavía no tiene documentos.");

        if (File.Exists(rutaZip))
            File.Delete(rutaZip);

        var agregados = 0;
        using (var zip = ZipFile.Open(rutaZip, ZipArchiveMode.Create))
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var documento in documentos)
            {
                var ruta = RutaAbsoluta(documento);
                if (!File.Exists(ruta))
                {
                    Log.Warning("Expediente {Paciente}: falta el archivo del documento {Documento}",
                        clienteId, documento.Id);
                    continue;
                }

                // Dentro del ZIP van con su nombre real; si se repite, se numera.
                var nombre = documento.Nombre;
                var intento = 1;
                while (!usados.Add(nombre))
                {
                    nombre = $"{Path.GetFileNameWithoutExtension(documento.Nombre)} ({++intento})" +
                             documento.Extension;
                }

                zip.CreateEntryFromFile(ruta, nombre, CompressionLevel.Optimal);
                agregados++;
            }
        }

        if (agregados == 0)
        {
            File.Delete(rutaZip);
            throw new InvalidOperationException(
                "Ninguno de los archivos está en el disco. Restaurá el respaldo de expedientes.");
        }

        await _auditoria.RegistrarAsync(AccionAuditoria.Consultar, DbNames.Cliente, clienteId,
            $"Expediente exportado: {agregados} documento(s)", ct);
        return agregados;
    }

    // ---------- Administración (solo Admin) ----------

    /// <summary>Cambia para qué sirve el papel sin volver a subirlo.</summary>
    public async Task CambiarTipoAsync(long documentoId, TipoDocumentoPaciente tipo,
        CancellationToken ct = default)
    {
        ExigirVer();
        var documento = await _documentos.ObtenerPorIdAsync(documentoId, ct)
            ?? throw new InvalidOperationException("El documento ya no existe.");

        await _documentos.CambiarTipoAsync(documentoId, tipo, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.DocumentoPaciente,
            documentoId, $"Documento '{documento.Nombre}' reclasificado como {EtiquetaTipo(tipo)}", ct);
    }

    /// <summary>ELIMINAR — exclusivo del Admin.</summary>
    public async Task EliminarAsync(long documentoId, CancellationToken ct = default)
    {
        ExigirAdmin("eliminar documentos del expediente");

        var documento = await _documentos.ObtenerPorIdAsync(documentoId, ct)
            ?? throw new InvalidOperationException("El documento ya no existe.");

        // El archivo se conserva en disco: el soft delete es reversible por
        // soporte, borrar el archivo no lo sería.
        await _documentos.EliminarAsync(documentoId, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.DocumentoPaciente,
            documentoId, $"Documento '{documento.Nombre}' quitado del expediente", ct);
    }

    /// <summary>
    /// RE-UBICAR — exclusivo del Admin: el papel era de otro paciente y hubo
    /// confusión en el mostrador. Mueve el archivo a la carpeta del destino.
    /// </summary>
    public async Task MoverAsync(long documentoId, long clienteDestinoId,
        CancellationToken ct = default)
    {
        ExigirAdmin("re-ubicar documentos");

        var documento = await _documentos.ObtenerPorIdAsync(documentoId, ct)
            ?? throw new InvalidOperationException("El documento ya no existe.");
        if (documento.ClienteId == clienteDestinoId)
            throw new InvalidOperationException("El documento ya está en ese expediente.");

        var destino = await _pacientes.ObtenerPorIdAsync(clienteDestinoId, ct)
            ?? throw new InvalidOperationException("El paciente de destino no existe.");

        var relativaNueva = $"{CarpetaDe(clienteDestinoId)}/{documento.Id}_{LimpiarNombre(documento.Nombre)}";
        var origen = RutaAbsoluta(documento);
        var rutaNueva = Path.Combine(_raiz, relativaNueva.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(rutaNueva)!);

        if (File.Exists(origen))
            File.Move(origen, rutaNueva, overwrite: true);

        await _documentos.MoverAsync(documentoId, clienteDestinoId, relativaNueva, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.DocumentoPaciente,
            documentoId, $"Documento '{documento.Nombre}' movido al expediente de {destino.Nombre}", ct);
    }

    /// <summary>
    /// Copia TODA la carpeta de expedientes a un ZIP. Lo usa el respaldo: el
    /// .sql solo trae la base, y los papeles del paciente valen tanto como los
    /// datos — restaurar sin ellos deja fichas apuntando a la nada.
    /// </summary>
    /// <returns>Ruta del ZIP, o null si todavía no hay ningún expediente.</returns>
    public static string? RespaldarTodoEnZip(string raiz, string carpetaDestino)
    {
        if (!Directory.Exists(raiz) || !Directory.EnumerateFileSystemEntries(raiz).Any())
            return null;   // no hay expedientes todavía: nada que respaldar

        var zip = Path.Combine(carpetaDestino,
            $"MED100_expedientes_{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
        if (File.Exists(zip))
            File.Delete(zip);

        ZipFile.CreateFromDirectory(raiz, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zip;
    }

    /// <summary>Cómo se lee el tipo de documento en pantalla.</summary>
    public static string EtiquetaTipo(TipoDocumentoPaciente tipo) => tipo switch
    {
        TipoDocumentoPaciente.Identificacion => "Identificación",
        TipoDocumentoPaciente.Seguro => "Seguro / ARS",
        TipoDocumentoPaciente.Consentimiento => "Consentimiento",
        TipoDocumentoPaciente.Referimiento => "Referimiento",
        TipoDocumentoPaciente.Estudio => "Estudio / resultado",
        TipoDocumentoPaciente.Factura => "Factura",
        TipoDocumentoPaciente.Otro => "Otro",
        _ => tipo.ToString()
    };

    // ---------- Puertas ----------

    private static void ExigirVer()
    {
        if (!SesionActual.TienePermiso("expedientes"))
            throw new UnauthorizedAccessException(
                "No tienes permiso para ver los expedientes de los pacientes.");
    }

    private static void ExigirAdmin(string accion)
    {
        if (!SesionActual.TienePermiso("usuarios"))
            throw new UnauthorizedAccessException($"Solo un administrador puede {accion}.");
    }

    private static void ValidarExtension(string extension)
    {
        if (!ExtensionesPermitidas.Contains(extension))
            throw new InvalidOperationException(
                $"El tipo de archivo '{extension}' no se permite en el expediente.\n\n" +
                "Se aceptan documentos (PDF, Word, Excel), imágenes (incluidas las de iPhone) " +
                "y comprimidos (ZIP, RAR).");
    }

    /// <summary>Saca del nombre lo que rompe una ruta de Windows.</summary>
    private static string LimpiarNombre(string nombre)
    {
        var limpio = string.Concat(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return limpio.Length > 120 ? limpio[^120..] : limpio;
    }

    private static string ArchivoPerdido(DocumentoPaciente documento) =>
        $"El archivo '{documento.Nombre}' ya no está en la carpeta de expedientes.\n\n" +
        "Puede que se haya movido a mano o que falte restaurar el respaldo de expedientes.";
}
