using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Acceso a la fila única de configuracion_negocio. La numeración de
/// facturas NO se lee desde aquí: se reserva con FOR UPDATE dentro de la
/// transacción de venta (VentaService).
/// </summary>
public class ConfiguracionNegocioRepository
{
    private readonly ConexionFactory _factory;

    public ConfiguracionNegocioRepository(ConexionFactory factory) => _factory = factory;

    public async Task<ConfiguracionNegocio> ObtenerAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT nombre_negocio, rnc, direccion, telefono, email, logo_ruta,
                   itbis_activo, itbis_tasa, redondeo, moneda_simbolo, formato_miles,
                   factura_prefijo, factura_siguiente, factura_formato,
                   mostrar_cliente_en_venta, ars_activo, turno_prefijo, turno_imprimir_auto,
                   archivar_factura_pdf, archivar_turno_pdf
            FROM {DbNames.ConfiguracionNegocio}
            WHERE id = 1;
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException(
                "configuracion_negocio está vacía. ¿Se ejecutó 001_create_schema.sql completo?");

        return new ConfiguracionNegocio
        {
            NombreNegocio = reader.GetString("nombre_negocio"),
            Rnc = Nulable(reader, "rnc"),
            Direccion = Nulable(reader, "direccion"),
            Telefono = Nulable(reader, "telefono"),
            Email = Nulable(reader, "email"),
            LogoRuta = Nulable(reader, "logo_ruta"),
            ItbisActivo = reader.GetBoolean("itbis_activo"),
            ItbisTasa = reader.GetDecimal("itbis_tasa"),
            Redondeo = reader.GetString("redondeo") switch
            {
                "peso" => ModoRedondeo.Peso,
                "arriba" => ModoRedondeo.Arriba,
                _ => ModoRedondeo.Centavo
            },
            MonedaSimbolo = reader.GetString("moneda_simbolo"),
            FormatoMiles = reader.GetString("formato_miles"),
            FacturaPrefijo = reader.GetString("factura_prefijo"),
            FacturaSiguiente = reader.GetInt64("factura_siguiente"),
            FacturaFormato = reader.GetString("factura_formato") == "con_anio"
                ? FormatoFactura.ConAnio : FormatoFactura.Simple,
            MostrarClienteEnVenta = reader.GetBoolean("mostrar_cliente_en_venta"),
            ArsActivo = reader.GetBoolean("ars_activo"),
            TurnoPrefijo = reader.GetString("turno_prefijo"),
            TurnoImprimirAuto = reader.GetBoolean("turno_imprimir_auto"),
            ArchivarFacturaPdf = reader.GetBoolean("archivar_factura_pdf"),
            ArchivarTurnoPdf = reader.GetBoolean("archivar_turno_pdf")
        };
    }

    /// <summary>
    /// Guarda la configuración editable. NO toca factura_siguiente: ese
    /// contador solo lo mueve la transacción de venta (spec §9.2).
    /// </summary>
    public async Task ActualizarAsync(ConfiguracionNegocio cfg, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.ConfiguracionNegocio}
            SET nombre_negocio = @nombre, rnc = @rnc, direccion = @direccion,
                telefono = @telefono, email = @email, logo_ruta = @logo,
                itbis_activo = @itbisActivo, itbis_tasa = @itbisTasa,
                redondeo = @redondeo, moneda_simbolo = @moneda, formato_miles = @formatoMiles,
                factura_prefijo = @prefijo, factura_formato = @formatoFactura,
                mostrar_cliente_en_venta = @mostrarCliente,
                ars_activo = @arsActivo,
                turno_prefijo = @turnoPrefijo, turno_imprimir_auto = @turnoAuto,
                archivar_factura_pdf = @archivarFactura, archivar_turno_pdf = @archivarTurno,
                updated_at = UTC_TIMESTAMP()
            WHERE id = 1;
            """;
        cmd.Parameters.AddWithValue("@nombre", cfg.NombreNegocio);
        cmd.Parameters.AddWithValue("@rnc", (object?)cfg.Rnc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@direccion", (object?)cfg.Direccion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telefono", (object?)cfg.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)cfg.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@logo", (object?)cfg.LogoRuta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@itbisActivo", cfg.ItbisActivo);
        cmd.Parameters.AddWithValue("@itbisTasa", cfg.ItbisTasa);
        cmd.Parameters.AddWithValue("@redondeo", cfg.Redondeo switch
        {
            ModoRedondeo.Peso => "peso",
            ModoRedondeo.Arriba => "arriba",
            _ => "centavo"
        });
        cmd.Parameters.AddWithValue("@moneda", cfg.MonedaSimbolo);
        cmd.Parameters.AddWithValue("@formatoMiles", cfg.FormatoMiles);
        cmd.Parameters.AddWithValue("@prefijo", cfg.FacturaPrefijo);
        cmd.Parameters.AddWithValue("@formatoFactura",
            cfg.FacturaFormato == FormatoFactura.ConAnio ? "con_anio" : "simple");
        cmd.Parameters.AddWithValue("@mostrarCliente", cfg.MostrarClienteEnVenta);
        cmd.Parameters.AddWithValue("@arsActivo", cfg.ArsActivo);
        cmd.Parameters.AddWithValue("@turnoPrefijo", cfg.TurnoPrefijo ?? string.Empty);
        cmd.Parameters.AddWithValue("@turnoAuto", cfg.TurnoImprimirAuto);
        cmd.Parameters.AddWithValue("@archivarFactura", cfg.ArchivarFacturaPdf);
        cmd.Parameters.AddWithValue("@archivarTurno", cfg.ArchivarTurnoPdf);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reserva atómica del siguiente número de factura (spec §9.2):
    /// SELECT ... FOR UPDATE + incremento en la MISMA transacción de la venta.
    /// </summary>
    public static async Task<long> ReservarNumeroFacturaAsync(
        MySqlConnection conexion, MySqlTransaction transaccion, CancellationToken ct = default)
    {
        long numero;
        using (var leer = conexion.CreateCommand())
        {
            leer.Transaction = transaccion;
            leer.CommandText =
                $"SELECT factura_siguiente FROM {DbNames.ConfiguracionNegocio} WHERE id = 1 FOR UPDATE;";
            numero = Convert.ToInt64(await leer.ExecuteScalarAsync(ct));
        }

        using var incrementar = conexion.CreateCommand();
        incrementar.Transaction = transaccion;
        incrementar.CommandText =
            $"UPDATE {DbNames.ConfiguracionNegocio} SET factura_siguiente = factura_siguiente + 1 WHERE id = 1;";
        await incrementar.ExecuteNonQueryAsync(ct);

        return numero;
    }

    private static string? Nulable(MySqlDataReader reader, string columna) =>
        reader.IsDBNull(reader.GetOrdinal(columna)) ? null : reader.GetString(columna);
}
