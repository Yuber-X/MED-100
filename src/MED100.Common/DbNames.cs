namespace MED100.Common;

/// <summary>
/// Nombres de tablas de la base de datos. Prohibido usar cadenas mágicas
/// para tablas en los repositorios — siempre referenciar estas constantes.
/// </summary>
public static class DbNames
{
    public const string Usuario = "usuario";
    public const string Rol = "rol";
    public const string Permiso = "permiso";
    public const string RolPermiso = "rol_permiso";
    public const string UsuarioPermiso = "usuario_permiso";
    public const string Sesion = "sesion";
    public const string Cliente = "cliente";
    public const string Producto = "producto";
    public const string Factura = "factura";
    public const string Detalle = "detalle";
    public const string CuadreCaja = "cuadre_caja";
    public const string Auditoria = "auditoria";
    public const string ConfiguracionNegocio = "configuracion_negocio";
    /// <summary>Fila única: desde cuándo corre el demo y si ya se activó.</summary>
    public const string Licencia = "licencia";

    // ---- Propias de la clínica (MED-100) ----
    public const string Medico = "medico";
    public const string MedicoHorario = "medico_horario";
    public const string Referidor = "referidor";
    public const string Procedimiento = "procedimiento";
    public const string Ars = "ars";
    public const string Cita = "cita";
    public const string Turno = "turno";
    /// <summary>Expediente digital del paciente: la ficha del archivo, no el archivo.</summary>
    public const string DocumentoPaciente = "documento_paciente";
}
