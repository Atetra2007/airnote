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
            ? new[] { "#1F2225", "#272B2E", "#191C1E", "#E3E8EC", "#A0AAB3", "#343B40", "#A4BED2", "#293B49", "#303D48", "#E6BC80", "#F0A4A4", "#17212A" }
            : new[] { "#FAFBFC", "#FFFFFF", "#F1F3F5", "#293139", "#68737D", "#DCE1E6", "#47647B", "#E8EFF5", "#E2E8EE", "#906019", "#B7443D", "#FFFFFF" };
        var names = new[] { "Canvas", "Surface", "Subtle", "Ink", "Muted", "Line", "Accent", "AccentSoft", "Selection", "Amber", "Error", "AccentInk" };
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
            Application.Current.Resources["SubtleBrush"] = SystemColors.WindowBrush;
            Application.Current.Resources["LineBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["InkBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["MutedBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["AccentBrush"] = SystemColors.HighlightBrush;
            Application.Current.Resources["AccentInkBrush"] = SystemColors.HighlightTextBrush;
            Application.Current.Resources["AccentSoftBrush"] = SystemColors.ControlBrush;
            Application.Current.Resources["SelectionBrush"] = SystemColors.ControlBrush;
        }
    }
}
