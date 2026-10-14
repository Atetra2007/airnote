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
            ? new[] { "#222326", "#28292D", "#2C2D32", "#EEEEEF", "#A4A5AE", "#38393F", "#C9CAD2", "#35363D", "#35363D", "#D6B78B", "#E5A4A0", "#222326", "#1C1D20", "#202124" }
            : new[] { "#F5F5F6", "#FFFFFF", "#EEEEF0", "#292A2E", "#686A73", "#DADBE0", "#555863", "#E8E8EC", "#E3E4E9", "#876022", "#AD423D", "#FFFFFF", "#EEEEF0", "#F8F8F9" };
        var names = new[] { "Canvas", "Surface", "Subtle", "Ink", "Muted", "Line", "Accent", "AccentSoft", "Selection", "Amber", "Error", "AccentInk", "Sidebar", "Chrome" };
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
            Application.Current.Resources["SidebarBrush"] = SystemColors.WindowBrush;
            Application.Current.Resources["ChromeBrush"] = SystemColors.WindowBrush;
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
