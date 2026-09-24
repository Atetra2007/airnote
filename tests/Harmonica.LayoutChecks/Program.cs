using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Harmonica.Core;
using Harmonica.Desktop;
using Harmonica.Desktop.Models;
using Harmonica.Desktop.Services;
using Harmonica.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--native")) return NativeOverlayChecks.Run();
        // Component layout/render checks only: never Show, register input, or automate desktop windows.
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/guide-validation/components");
        Directory.CreateDirectory(output);
        var app = new App(); app.InitializeComponent();
        var main = new MainWindow(new LibraryStore(Path.Combine(output, "unused-data"), AppContext.BaseDirectory));
        ((TextBox)main.FindName("TitleInput")).Text = "跟谱测试 · 长音、休止与鼠标组合";
        ((TextBox)main.FindName("BodyInput")).Text = "（#3）:4 0 【2】 #1 【【1】】 5_ 6_ 7";
        Invoke(main, "ParseEditor");
        ((RadioButton)main.FindName("GuideModeButton")).IsChecked = true;
        ((FrameworkElement)main.FindName("GuideControls")).Visibility = Visibility.Visible;
        ((TextBlock)main.FindName("GuideRangeLabel")).Text = "A 10000 → B 10000";
        var mainNames = new[] { "PreviewMode", "GameMode", "GuideModeButton", "PlayButton", "StopButton", "BpmInput", "VolumeSlider", "GuideKind", "GuideSpeed", "AdjustGuideButton", "GuideOpacity", "GuideEventNumber", "SeekGuideButton", "SetGuideAButton", "SetGuideBButton", "GuideLoop", "ClearGuideRangeButton", "GuideRangeLabel", "ScoreView" };
        var checks = new List<object>();
        var settings = new UserSettings { StartHotkey = "F7", StopHotkey = "F8" };
        var dialog = new SettingsDialog(settings, 20, output);
        typeof(SettingsDialog).GetMethod("Apply_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        if (((TextBlock)dialog.FindName("ErrorText")).Visibility != Visibility.Visible || settings.StartHotkey != "F7")
            throw new Exception("Hotkey collision must show inline error without changing existing settings");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeService.Apply(theme);
            foreach (var dpi in new[] { 1d, 1.5, 2 })
            {
                var root = (FrameworkElement)dialog.Content; var size = new Size(444, 531);
                Layout(root, size, dpi);
                CheckRects(dialog, root, ["StartInput", "StopInput", "OverlayInput", "GapInput", "ErrorText", "CancelSettingsButton", "ApplySettingsButton"], size);
                Save(root, size, dpi, Path.Combine(output, $"settings-{theme}-{dpi:0.0}.png"));
                checks.Add(new { surface = "settings", theme, dpi, passed = true });
            }
            foreach (var size in new[] { new Size(864, 561), new Size(1064, 681) })
            foreach (var dpi in new[] { 1d, 1.5, 2 })
            {
                var root = (FrameworkElement)main.Content;
                Layout(root, size, dpi); CheckRects(main, root, mainNames, size);
                Save(root, size, dpi, Path.Combine(output, $"main-{theme}-{size.Width}-{dpi:0.0}.png"));
                checks.Add(new { surface = "main", theme, size.Width, size.Height, dpi, passed = true });
            }
        }
        var clock = new ManualClock();
        var score = ScoreCodec.Parse(new("跟谱测试 · 长音与鼠标组合", 100, 20, "（#3）:4 0 【2】 #1 【【1】】 5_ 6_ 7"));
        var engine = new GuideEngine(score, GuideMode.WaitForKey, clock: clock);
        engine.Start(); clock.Seconds = 3; engine.Update();
        var overlay = new GuideOverlayWindow(new GuidePreferences());
        foreach (var size in new[] { new Size(440, 240), new Size(560, 280) })
        foreach (var dpi in new[] { 1d, 1.5, 2 })
        foreach (var opacity in new[] { .35, .75, .95 })
        {
            overlay.Width = size.Width; overlay.Height = size.Height;
            overlay.SetBackgroundOpacity(opacity); overlay.Present(engine, "Ctrl+F6", "Ctrl+F7", "Ctrl+F8");
            var root = (FrameworkElement)overlay.Content;
            Layout(root, size, dpi);
            CheckRects(overlay, root, ["SongTitle", "ModeLabel", "Notes", "CurrentKey", "NextKey", "ShortcutLabel"], size);
            foreach (var name in new[] { "CurrentKey", "ShortcutLabel", "ModeLabel" })
            {
                var text = (TextBlock)overlay.FindName(name);
                var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.White, dpi);
                if (formatted.Width > text.ActualWidth + 1) throw new Exception($"{name} text clipped at {size}, DPI {dpi}");
            }
            Save(root, size, dpi, Path.Combine(output, $"overlay-{size.Width}-{dpi:0.0}-{opacity:0.00}.png"));
            checks.Add(new { surface = "overlay", size.Width, size.Height, dpi, opacity, passed = true });
        }
        // Exercise completion before the end of the full score.
        engine.Pause(); engine.SetMode(GuideMode.Rhythm); engine.SetLoop(0, 0, false);
        engine.Start(); clock.Seconds += 20; engine.Update();
        overlay.Present(engine, "F6", "F7", "F8");
        if (((TextBlock)overlay.FindName("CurrentKey")).Text != "本次练习完成") throw new Exception("Completed range leaked the next note");

        var notes = Enumerable.Range(0, 100_000).Select(i => new ScoreEvent(i % 7 + 1, 0, false, .25, 0, 1)).ToArray();
        var large = new GuideEngine(new ParsedScore(new("Long render", 120, 20, ""), notes), clock: clock);
        large.SeekToEvent(50_000); overlay.Present(large, "F6", "F7", "F8");
        var view = (GuideNoteView)overlay.FindName("Notes");
        view.Measure(new(408, 144)); view.Arrange(new(0, 0, 408, 144)); view.UpdateLayout();
        // Force 1,200 actual drawing passes over a 100k score. No production timer runs here.
        var bitmap = new RenderTargetBitmap(408, 144, 96, 96, PixelFormats.Pbgra32);
        GC.Collect(); var before = GC.GetTotalMemory(true); var watch = Stopwatch.StartNew();
        for (var i = 0; i < 1200; i++) { view.Progress = large.Snapshot with { PositionBeats = 12_500 + i / 60d }; view.InvalidateVisual(); view.UpdateLayout(); bitmap.Render(view); bitmap.Clear(); }
        watch.Stop(); GC.Collect(); var retained = GC.GetTotalMemory(true) - before;
        if (retained > 20_000_000) throw new Exception($"Unexpected retained drawing memory: {retained}");
        if (main.IsLoaded || main.IsVisible || overlay.IsLoaded || overlay.IsVisible || dialog.IsLoaded) throw new Exception("Component check showed a desktop window");
        var report = new { scope = "component layout/render, not live DPI/game validation", checks, longScore = new { events = notes.Length, frames = 1200, milliseconds = watch.ElapsedMilliseconds, retainedManagedBytes = retained } };
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {checks.Count} layout/render cases; 100,000-note render x1200 in {watch.ElapsedMilliseconds} ms; retained managed delta {retained} bytes.");
        return 0;
    }
    private static void Invoke(object value, string name) => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, null);
    private static void Layout(FrameworkElement root, Size size, double dpi)
    { VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi)); root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); }
    private static void CheckRects(Window window, FrameworkElement root, string[] names, Size size)
    {
        var rectangles = names.Select(name =>
        {
            var element = (FrameworkElement)window.FindName(name);
            var rect = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            if (rect.Width <= 0 || rect.Height <= 0 || rect.Left < -.1 || rect.Top < -.1 || rect.Right > size.Width + .1 || rect.Bottom > size.Height + .1)
                throw new Exception($"{name} outside {size}: {rect}");
            return (name, rect);
        }).ToArray();
        for (var i = 0; i < rectangles.Length; i++) for (var j = i + 1; j < rectangles.Length; j++)
        {
            var overlap = Rect.Intersect(rectangles[i].rect, rectangles[j].rect);
            if (!overlap.IsEmpty && overlap.Width > .1 && overlap.Height > .1) throw new Exception($"{rectangles[i].name} overlaps {rectangles[j].name}: {overlap}");
        }
    }
    private static void Save(FrameworkElement root, Size size, double dpi, string path)
    {
        var bitmap = new RenderTargetBitmap((int)(size.Width * dpi), (int)(size.Height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class ManualClock : IPlaybackClock
    { public double Seconds { get; set; } public Task DelayAsync(TimeSpan delay, CancellationToken token) => throw new NotSupportedException(); }
}
