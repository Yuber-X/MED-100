using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MED100.Models;
using SkiaSharp;

namespace MED100.ViewModels;

/// <summary>
/// Construye el gráfico de barras de ventas por día (Panel y Reportes).
/// Los días sin ventas aparecen en 0 — así el hueco se ve, en vez de que el
/// gráfico "salte" del 3 al 7 y parezca continuo.
///
/// Los valores del gráfico son double porque LiveCharts trabaja así: es SOLO
/// presentación. El dinero real nunca sale de decimal (BD y cálculos).
/// </summary>
internal static class GraficoVentas
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");
    private static readonly SKColor ColorPrimario = SKColor.Parse("#4F46E5");
    private static readonly SKColor ColorEtiquetas = SKColor.Parse("#888780");

    public static (ISeries[] Series, Axis[] XAxes, Axis[] YAxes) Construir(
        IReadOnlyList<VentaDiaria> ventas, DateOnly desde, DateOnly hasta, string nombreSerie = "Ventas")
    {
        var porDia = ventas.ToDictionary(v => v.Fecha, v => v.Monto);
        var dias = Math.Max(1, hasta.DayNumber - desde.DayNumber + 1);

        var valores = new double[dias];
        var etiquetas = new string[dias];
        for (var i = 0; i < dias; i++)
        {
            var fecha = desde.AddDays(i);
            valores[i] = (double)porDia.GetValueOrDefault(fecha, 0m);
            // Rangos largos: solo el día; rangos cortos: día/mes
            etiquetas[i] = dias > 31
                ? fecha.ToString("dd/MM", CulturaRd)
                : fecha.Day.ToString(CulturaRd);
        }

        ISeries[] series =
        [
            new ColumnSeries<double>
            {
                Values = valores,
                Fill = new SolidColorPaint(ColorPrimario),
                Rx = 4,
                Ry = 4,
                Name = nombreSerie
            }
        ];

        Axis[] xAxes =
        [
            new Axis
            {
                Labels = etiquetas,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ColorEtiquetas),
                SeparatorsPaint = null
            }
        ];

        Axis[] yAxes =
        [
            new Axis
            {
                MinLimit = 0,
                TextSize = 10,
                LabelsPaint = new SolidColorPaint(ColorEtiquetas),
                Labeler = valor => valor >= 1000
                    ? $"{valor / 1000:0.#}k"
                    : valor.ToString("0", CulturaRd)
            }
        ];

        return (series, xAxes, yAxes);
    }
}
