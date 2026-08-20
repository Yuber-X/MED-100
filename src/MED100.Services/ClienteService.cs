using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Reglas de negocio de PACIENTES (tabla <c>cliente</c>). Cédula OPCIONAL: en
/// una clínica llega gente sin documento encima y el registro no se puede
/// trabar por eso; si viene, se normaliza (000-0000000-0) y se exige única.
/// Requiere permiso clientes_editar para mutaciones (el Cajero solo consulta).
///
/// Nada de esto guarda información clínica: ver CLAUDE.md §1.1.
/// </summary>
public class ClienteService
{
    /// <summary>Tope de edad razonable. Freno al dedazo en el año, no un límite del negocio.</summary>
    private const int EdadMaximaAnios = 130;

    private readonly ClienteRepository _clientes;
    private readonly HistorialRepository _historial;
    private readonly AuditoriaService _auditoria;

    public ClienteService(ClienteRepository clientes, HistorialRepository historial,
        AuditoriaService auditoria)
    {
        _clientes = clientes;
        _historial = historial;
        _auditoria = auditoria;
    }

    public Task<List<Cliente>> ObtenerTodosAsync(CancellationToken ct = default) =>
        _clientes.ObtenerTodosAsync(ct);

    public Task<Cliente?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _clientes.ObtenerPorIdAsync(id, ct);

    /// <summary>
    /// Todo lo que pasó con este paciente: citas, turnos, facturas y los
    /// procedimientos que se le cobraron.
    ///
    /// Es CONSULTA, así que basta el permiso <c>clientes</c>: el cajero que
    /// atiende el mostrador tiene que poder contestar "¿cuándo vino la última
    /// vez?" sin permiso de edición. Queda en auditoría porque son datos
    /// personales y la Ley 172-13 exige poder decir quién los miró.
    /// </summary>
    public async Task<HistorialPaciente> ObtenerHistorialAsync(long clienteId,
        CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("clientes"))
            throw new InvalidOperationException("No tienes permiso para ver la ficha de los pacientes.");

        var historial = await _historial.ObtenerAsync(clienteId, ct)
            ?? throw new InvalidOperationException("El paciente no existe o fue eliminado.");

        await _auditoria.RegistrarAsync(AccionAuditoria.Consultar, DbNames.Cliente, clienteId,
            $"Ficha consultada: {historial.Paciente.Nombre}", ct);
        return historial;
    }

    public async Task<long> CrearAsync(ClienteDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: null, ct);
        var id = await _clientes.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Cliente, id,
            $"Paciente creado: {limpios.Nombre}", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, ClienteDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: id, ct);
        await _clientes.ActualizarAsync(id, limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Cliente, id,
            $"Paciente modificado: {limpios.Nombre}", ct);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        var cliente = await _clientes.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El paciente no existe o ya fue eliminado.");
        await _clientes.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Cliente, id,
            $"Paciente eliminado: {cliente.Nombre}", ct);
    }

    private async Task<ClienteDatos> ValidarAsync(ClienteDatos datos, long? exceptoId, CancellationToken ct)
    {
        ValidarPermisoEdicion();

        if (string.IsNullOrWhiteSpace(datos.Nombre))
            throw new ArgumentException("El nombre del paciente es obligatorio.");

        var cedula = string.IsNullOrWhiteSpace(datos.Cedula) ? null : NormalizarCedula(datos.Cedula);
        if (cedula is not null && await _clientes.ExisteCedulaAsync(cedula, exceptoId, ct))
            throw new ArgumentException($"Ya existe un paciente con la cédula {cedula}.");

        var email = Limpiar(datos.Email);
        if (email is not null)
        {
            // Se valida acá y no solo en la UI porque de este correo depende el
            // recordatorio de cita: un correo mal escrito es una cita perdida
            // sin que nadie se entere (el envío falla en silencio, de noche).
            if (!EsEmailPlausible(email))
                throw new ArgumentException($"«{email}» no parece un correo válido.");
            if (email.Length > 150)
                throw new ArgumentException("El correo no puede pasar de 150 caracteres.");
        }

        ValidarNacimiento(datos.FechaNacimiento);

        return datos with
        {
            Cedula = cedula,
            Nombre = datos.Nombre.Trim(),
            Telefono = Limpiar(datos.Telefono),
            Email = email,
            Direccion = Limpiar(datos.Direccion),
            Notas = Limpiar(datos.Notas)
        };
    }

    /// <summary>
    /// La fecha de nacimiento no puede estar en el futuro ni a 130 años de
    /// distancia. No es purismo: el campo se teclea a mano y un año mal puesto
    /// (1925 en vez de 2025) manda a un bebé al grupo de los adultos mayores.
    /// </summary>
    private static void ValidarNacimiento(DateOnly? nacimiento)
    {
        if (nacimiento is not { } fecha)
            return;

        var hoy = FechaNegocio.Hoy;
        if (fecha > hoy)
            throw new ArgumentException("La fecha de nacimiento no puede ser futura.");
        if (fecha < hoy.AddYears(-EdadMaximaAnios))
            throw new ArgumentException(
                $"La fecha de nacimiento es de hace más de {EdadMaximaAnios} años. Revisá el año.");
    }

    /// <summary>
    /// Validación deliberadamente laxa: algo@algo.algo. No se usa una regex
    /// "completa" de RFC 5322 porque rechaza correos legítimos y no atrapa el
    /// error real, que es el dedazo. Quien decide de verdad si el correo sirve
    /// es el servidor cuando se manda el recordatorio.
    /// </summary>
    public static bool EsEmailPlausible(string email)
    {
        var partes = email.Split('@');
        if (partes.Length != 2)
            return false;
        var (usuario, dominio) = (partes[0], partes[1]);
        if (usuario.Length == 0 || dominio.Length < 3)
            return false;
        if (email.Any(char.IsWhiteSpace))
            return false;

        var punto = dominio.LastIndexOf('.');
        return punto > 0 && punto < dominio.Length - 1;
    }

    private static void ValidarPermisoEdicion()
    {
        if (!SesionActual.TienePermiso("clientes_editar"))
            throw new InvalidOperationException("No tienes permiso para crear o editar pacientes.");
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>
    /// 11 dígitos → formato 000-0000000-0; otra cosa (pasaporte) se acepta
    /// tal cual hasta 20 caracteres. Patrón heredado de PrestControl.
    /// </summary>
    public static string NormalizarCedula(string cedula)
    {
        var limpia = cedula.Trim();
        var digitos = new string(limpia.Where(char.IsDigit).ToArray());

        // Cédula dominicana: 11 dígitos (admitiendo espacios/guiones de relleno).
        // Si hay letras u otros símbolos es un pasaporte y se respeta tal cual.
        var soloRelleno = limpia.All(c => char.IsDigit(c) || c == '-' || c == ' ');
        if (digitos.Length == 11 && soloRelleno)
            return $"{digitos[..3]}-{digitos[3..10]}-{digitos[10..]}";

        if (limpia.Length > 20)
            throw new ArgumentException("La cédula o pasaporte no puede superar 20 caracteres.");
        return limpia;
    }
}
