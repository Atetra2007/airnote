using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Harmonica.Desktop.Services;

public static class ThemeService
{
    public static bool Dark { get; private set; }
    public static void Apply(string choice)
    {
        bool systemDark;
        try { systemDark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0; }
        catch { systemDark = false; }
        Dark = choice == "Dark" || choice != "Light" && systemDark;
        var colors = Dark
            ? new[] { "#191D20", "#22272B", "#2C3337", "#EDF1EF", "#A6B2AC", "#35403A", "#8AD8BC", "#244C3C", "#17382B", "#F1BC79", "#F0A4A4" }
            : new[] { "#F3F5F3", "#FFFFFF", "#EBF0EB", "#24372E", "#728178", "#DFE6DF", "#2F7658", "#E2F1E7", "#D0E7D8", "#966622", "#B74C44" };
        var names = new[] { "Canvas", "Surface", "Subtle", "Ink", "Muted", "Line", "Accent", "AccentSoft", "Selection", "Amber", "Error" };
        for (int i = 0; i < names.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze();
            Application.Current.Resources[names[i] + "Brush"] = brush;
        }
        if (SystemParameters.HighContrast)
        {
            Application.Current.Resources["CanvasBrush"] = SystemColors.WindowBrush;
            Application.Current.Resources["SurfaceBrush"] = SystemColors.WindowBrush;
            Application.Current.Resources["InkBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["MutedBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["AccentBrush"] = SystemColors.HighlightBrush;
            Application.Current.Resources["SelectionBrush"] = SystemColors.HighlightBrush;
        }
    }
}
