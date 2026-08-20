using System.IO;
using ClosedXML.Excel;
using MED100.Common;
using MED100.Data;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Exportación completa a Excel (.xlsx): una hoja por tabla (Clientes,
/// Productos, Facturas, Detalle, Cuadres, Usuarios, Auditoría).
/// Portado de PrestControl, con las tablas del POS.
///
/// Para MIGRAR de equipo el camino es Respaldar/Restaurar (.sql conserva ids
/// y relaciones exactas); el Excel es para consulta humana y contabilidad.
/// </summary>
public class ExportacionService
{
    private readonly ExportacionRepository _repositorio;

    public ExportacionService(ExportacionRepository repositorio) => _repositorio = repositorio;

    public async Task ExportarAsync(string rutaDestino, CancellationToken ct = default)
    {
        var tablas = await _repositorio.ObtenerTodoAsync(ct);

        using var libro = new XLWorkbook();
        foreach (var tabla in tablas)
        {
            var hoja = libro.AddWorksheet(tabla.Nombre);

            for (var col = 0; col < tabla.Encabezados.Count; col++)
            {
                var celda = hoja.Cell(1, col + 1);
                celda.Value = tabla.Encabezados[col];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF0FE");
            }

            for (var fila = 0; fila < tabla.Filas.Count; fila++)
            {
                var datos = tabla.Filas[fila];
                for (var col = 0; col < datos.Length; col++)
                {
                    var celda = hoja.Cell(fila + 2, col + 1);
                    celda.Value = datos[col] switch
                    {
                        null => Blank.Value,
                        decimal d => d,
                        DateTime f => f,
                        DateOnly f => f.ToDateTime(TimeOnly.MinValue),
                        bool b => b,
                        long l => l,
                        int i => i,
                        uint u => u,
                        ulong ul => (double)ul,
                        var otro => otro.ToString() ?? string.Empty
                    };
                }
            }

            hoja.SheetView.FreezeRows(1);
            hoja.Columns().AdjustToContents(1, Math.Min(tabla.Filas.Count + 1, 200));
        }

        libro.SaveAs(rutaDestino);
        Log.Information("Exportación Excel generada en {Ruta} ({Tablas} hojas)", rutaDestino, tablas.Count);
    }

    /// <summary>
    /// Export automático programado: corre al iniciar la app si está activo
    /// y ya pasaron los días configurados desde la última corrida.
    /// NUNCA lanza — un fallo del export no debe impedir usar la aplicación.
    /// </summary>
    public async Task EjecutarAutomaticoSiTocaAsync(AjustesLocales ajustes)
    {
        try
        {
            if (!ajustes.ExportAutomaticoActivo || string.IsNullOrWhiteSpace(ajustes.ExportAutomaticoCarpeta))
                return;

            var dias = Math.Max(1, ajustes.ExportAutomaticoCadaDias);
            if (ajustes.UltimaExportacionUtc is { } ultima &&
                (DateTime.UtcNow - ultima).TotalDays < dias)
                return;

            Directory.CreateDirectory(ajustes.ExportAutomaticoCarpeta);
            var ruta = Path.Combine(ajustes.ExportAutomaticoCarpeta,
                $"MED100_Export_{FechaNegocio.Hoy:yyyy-MM-dd}.xlsx");

            await ExportarAsync(ruta);
            ajustes.UltimaExportacionUtc = DateTime.UtcNow;
            ajustes.Guardar();
            Log.Information("Export automático completado: {Ruta}", ruta);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falló el export automático a Excel");
        }
    }
}
