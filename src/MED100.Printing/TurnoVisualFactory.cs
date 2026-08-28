using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MED100.Models;

namespace MED100.Printing;

/// <summary>
/// El papelito del turno de la sala de espera, 80mm.
///
/// Es un documento APARTE del recibo de la factura, no una sección de él: el
/// paciente se lleva el turno al entrar, cuando todavía no hay nada que cobrar
/// (CLAUDE.md §1.3.4). Por eso vive en su propia factory.
///
/// El número va enorme, que es todo lo que importa: se lee de lejos, con el
/// papel en la mano y mirando la pantalla de la sala.
/// </summary>
public static class TurnoVisualFactory
{
    private const double Ancho = 302;   // 80mm @96dpi
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Mono = new("Consolas");

    public static FrameworkElement Crear(Turno turno, string etiqueta, ConfiguracionNegocio negocio)
    {
        var panel = new StackPanel { Width = Ancho, Background = Brushes.White };
        var margen = new Thickness(12, 2, 12, 2);

        panel.Children.Add(Texto(negocio.NombreNegocio, 14, FontWeights.Bold,
            TextAlignment.Center, new Thickness(12, 14, 12, 2)));
        if (!string.IsNullOrWhiteSpace(negocio.Direccion))
            panel.Children.Add(Texto(negocio.Direccion, 10, FontWeights.Normal, TextAlignment.Center, margen));

        panel.Children.Add(Separador());
        panel.Children.Add(Texto("SU TURNO", 12, FontWeights.Normal, TextAlignment.Center,
            new Thickness(12, 8, 12, 0)));

        // El número: lo único que se mira de verdad.
        panel.Children.Add(Texto(etiqueta, 72, FontWeights.Bold, TextAlignment.Center,
            new Thickness(12, 4, 12, 4)));

        panel.Children.Add(Separador());

        var creadoLocal = TimeZoneInfo.ConvertTimeFromUtc(turno.CreatedAtUtc, ZonaRd());
        panel.Children.Add(Fila("Fecha:", creadoLocal.ToString("dd/MM/yyyy", CulturaDo)));
        panel.Children.Add(Fila("Hora:", creadoLocal.ToString("hh:mm tt", CulturaDo)));

        // Solo se imprime lo que se sabe. Un turno que se dio en la puerta no
        // tiene paciente ni médico todavía, y dejar "—" impreso no aporta nada.
        if (!string.IsNullOrWhiteSpace(turno.PacienteNombre))
            panel.Children.Add(Fila("Nombre:", turno.PacienteNombre));
        if (!string.IsNullOrWhiteSpace(turno.MedicoNombre))
            panel.Children.Add(Fila("Médico:", turno.MedicoNombre));

        panel.Children.Add(Separador());
        panel.Children.Add(Texto("Espere a que se llame su número.", 11, FontWeights.Normal,
            TextAlignment.Center, new Thickness(12, 4, 12, 16)));

        // Medir y organizar para poder imprimir sin mostrarlo en pantalla
        panel.Measure(new Size(Ancho, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, Ancho, panel.DesiredSize.Height));
        return panel;
    }

    private static TimeZoneInfo ZonaRd()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time"); }
    }

    private static TextBlock Texto(string texto, double tamano, FontWeight peso,
        TextAlignment alineacion, Thickness margen) => new()
    {
        Text = texto,
        FontFamily = Mono,
        FontSize = tamano,
        FontWeight = peso,
        Foreground = Brushes.Black,
        TextAlignment = alineacion,
        TextWrapping = TextWrapping.Wrap,
        Margin = margen
    };

    private static Grid Fila(string izquierda, string derecha)
    {
        var grid = new Grid { Margin = new Thickness(12, 1, 12, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var izq = Texto(izquierda, 11, FontWeights.Normal, TextAlignment.Left, new Thickness(0));
        var der = Texto(derecha, 11, FontWeights.Normal, TextAlignment.Right, new Thickness(0));
        Grid.SetColumn(der, 1);
        grid.Children.Add(izq);
        grid.Children.Add(der);
        return grid;
    }

    private static TextBlock Separador() =>
        Texto(new string('-', 38), 11, FontWeights.Normal, TextAlignment.Center, new Thickness(12, 4, 12, 4));
}
