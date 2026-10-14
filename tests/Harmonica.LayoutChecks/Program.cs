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
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        // Component layout/render checks only: never Show, register input, or automate desktop windows.
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/guide-validation/components");
        Directory.CreateDirectory(output);
        var app = new App(); app.InitializeComponent();
        SearchLayoutChecks.Run(output);
        var main = new MainWindow(new LibraryStore(Path.Combine(output, "unused-data"), AppContext.BaseDirectory));
        ((TextBox)main.FindName("TitleInput")).Text = "跟谱测试 · 长音、休止与鼠标组合";
        ((TextBox)main.FindName("BodyInput")).Text = "（#3）:4 0 【2】 #1 | 【【1】】 5_ 6_ 7";
        Invoke(main, "ParseEditor");
        CheckStudioBehavior(main);
        ((ComboBox)main.FindName("PlaybackModePicker")).SelectedIndex = 2;
        ((FrameworkElement)main.FindName("GuideControls")).Visibility = Visibility.Visible;
        ((TextBlock)main.FindName("GuideRangeLabel")).Text = "A 10000 → B 10000";
        var mainNames = new[] { "PlaybackModePicker", "PlayButton", "StopButton", "BpmInput", "VolumeSlider", "GuideKind", "GuideSpeed", "AdjustGuideButton", "GuideOpacity", "GuideEventNumber", "SeekGuideButton", "SetGuideAButton", "SetGuideBButton", "GuideLoop", "ClearGuideRangeButton", "GuideRangeLabel", "ScoreScroll", "QualityFilter", "GameNoteCheckButton", "AllFilter", "LocalFilter", "FavoriteFilter", "RecentFilter", "SortPicker", "SearchInput", "SongList", "ImportButton", "ResultCount", "SettingsButton" };
        var checks = new List<object>();
        var settings = new UserSettings { StartHotkey = "F7", StopHotkey = "F8" };
        var dialog = new SettingsDialog(settings, 20, output);
        typeof(SettingsDialog).GetMethod("Apply_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        if (((TextBlock)dialog.FindName("ErrorText")).Visibility != Visibility.Visible || settings.StartHotkey != "F7")
            throw new Exception("Hotkey collision must show inline error without changing existing settings");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeService.Apply(theme);
            CheckPrimaryContrast();
            foreach (var dpi in new[] { 1d, 1.5, 2 })
            {
                var root = (FrameworkElement)dialog.Content; var size = new Size(444, 621);
                Layout(root, size, dpi);
                CheckRects(dialog, root, ["StartInput", "StopInput", "OverlayInput", "GapInput", "GameGapInput", "ErrorText", "CancelSettingsButton", "ApplySettingsButton"], size);
                Save(root, size, dpi, Path.Combine(output, $"settings-{theme}-{dpi:0.0}.png"));
                checks.Add(new { surface = "settings", theme, dpi, passed = true });
            }
            foreach (var size in new[] { new Size(924, 561), new Size(1144, 721) })
            foreach (var dpi in new[] { 1d, 1.5, 2 })
            {
                var root = (FrameworkElement)main.Content;
                Layout(root, size, dpi); CheckRects(main, root, mainNames, size);
                Save(root, size, dpi, Path.Combine(output, $"main-{theme}-{size.Width}-{dpi:0.0}.png"));
                checks.Add(new { surface = "main", theme, size.Width, size.Height, dpi, passed = true });
            }
        }
        CheckExpandedEditorLayouts(main, output);
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
    private static void CheckStudioBehavior(MainWindow window)
    {
        var mode = (ComboBox)window.FindName("PlaybackModePicker");
        if (mode.Items.Count != 3 || mode.SelectedIndex != 0) throw new Exception("Studio must start in local preview and retain all three modes");
        var volume = (Slider)window.FindName("VolumeSlider");
        volume.ApplyTemplate();
        var oldVolume = volume.Value;
        Slider.IncreaseLarge.Execute(null, volume);
        if (volume.Value <= oldVolume) throw new Exception("Styled volume slider lost its native commands");
        volume.Value = oldVolume;
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        bool GetMode(string name) => (bool)typeof(MainWindow).GetProperty(name, flags)!.GetValue(window)!;
        for (var i = 0; i < 3; i++)
        {
            mode.SelectedIndex = i;
            Invoke(window, "UpdateAvailability");
            if (GetMode("InGameMode") != (i == 1) || GetMode("InGuideMode") != (i == 2)) throw new Exception("Playback picker routes to the wrong mode");
            if ((((FrameworkElement)window.FindName("GuideControls")).Visibility == Visibility.Visible) != (i == 2)) throw new Exception("Guide controls do not follow the selected mode");
        }
        mode.SelectedIndex = 0;
        typeof(MainWindow).GetField("busy", flags)!.SetValue(window, true);
        Invoke(window, "UpdateAvailability");
        if (mode.IsEnabled || ((Button)window.FindName("SaveCopyButton")).IsEnabled) throw new Exception("Busy playback must lock mode switching and saving");
        typeof(MainWindow).GetField("busy", flags)!.SetValue(window, false);
        Invoke(window, "UpdateAvailability");
        foreach (var editing in new[] { false, true, false })
        {
            typeof(MainWindow).GetField("editMode", flags)!.SetValue(window, editing);
            Invoke(window, "SetEditorVisibility");
            if (((TextBox)window.FindName("TitleInput")).IsReadOnly == editing) throw new Exception("Title editing leaked out of edit mode");
            foreach (var name in new[] { "SaveButton", "SaveCopyButton", "BodyInput" })
                if ((((FrameworkElement)window.FindName(name)).Visibility == Visibility.Visible) != editing) throw new Exception("Editor action visibility is incorrect: " + name);
        }
        // A tempo change outside edit mode must still expose Save, without exposing the text editor.
        typeof(MainWindow).GetField("dirty", flags)!.SetValue(window, true);
        Invoke(window, "UpdateEditorActions");
        if (((Button)window.FindName("SaveButton")).Visibility != Visibility.Visible || ((TextBox)window.FindName("BodyInput")).Visibility != Visibility.Collapsed) throw new Exception("Tempo edits cannot be saved from reading mode");
        typeof(MainWindow).GetField("dirty", flags)!.SetValue(window, false);
        Invoke(window, "UpdateEditorActions");
        var tiles = (List<Border>)typeof(MainWindow).GetField("noteTiles", flags)!.GetValue(window)!;
        if (tiles.Count != 8 || ((WrapPanel)window.FindName("NotesPanel")).Children.Count != 9) throw new Exception("Source separator changed event indexing");
        var parsed = (ParsedScore)typeof(MainWindow).GetField("parsed", flags)!.GetValue(window)!;
        for (var i = 0; i < tiles.Count; i++)
            if (((TextBlock)((StackPanel)tiles[i].Child).Children[0]).Text != parsed.Events[i].Label) throw new Exception("Studio changed a pitch label");
        typeof(MainWindow).GetMethod("Playback_Progress", flags)!.Invoke(window, [new PlaybackProgress("Playing", 4, 8, 1, 5)]);
        if (tiles[4].Background != Application.Current.Resources["AccentSoftBrush"]) throw new Exception("Playback highlight lost event indexing after separators");
        Invoke(window, "ClearHighlight");
        if (window.IsLoaded || window.IsVisible) throw new Exception("Studio behavior check showed a window");
        Console.WriteLine("PASS studio mode routing, busy locks, editor visibility, pitch identity and highlight indexing.");
    }
    private static void CheckPrimaryContrast()
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value) { var n = value / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var foreground = Luminance(((SolidColorBrush)Application.Current.Resources["AccentInkBrush"]).Color);
        var background = Luminance(((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color);
        if ((Math.Max(foreground, background) + .05) / (Math.Min(foreground, background) + .05) < 4.5) throw new Exception("Primary action text contrast is too low");
    }
    private static void CheckExpandedEditorLayouts(MainWindow window, string output)
    {
        var root = (FrameworkElement)window.Content;
        var details = (Expander)window.FindName("DetailsExpander");
        ((TextBlock)window.FindName("SourceText")).Text = string.Join("\n", Enumerable.Repeat("来源与版本记录；校对状态对应实际曲谱文件。", 12));
        ((TextBox)window.FindName("LogBox")).Text = "布局检查日志。";
        var checks = new List<object>();
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var size in new[] { new Size(924, 561), new Size(1144, 721) })
        foreach (var mode in new[] { 0, 2 })
        foreach (var editing in new[] { false, true })
        {
            ThemeService.Apply(theme);
            ((ComboBox)window.FindName("PlaybackModePicker")).SelectedIndex = mode;
            Invoke(window, "UpdateAvailability");
            typeof(MainWindow).GetField("editMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, editing);
            Invoke(window, "SetEditorVisibility");
            details.IsExpanded = true;
            Layout(root, size, 1);
            CheckRects(window, root, ["DetailsExpander", "EditButton", "PlaybackModePicker", "PlayButton", "StopButton", editing ? "BodyInput" : "ScoreScroll"], size);
            if (editing) CheckRects(window, root, ["SaveButton", "SaveCopyButton", "TitleInput", "BodyInput"], size);
            CheckRects(window, root, ["DetailsScroll"], size);
            checks.Add(new { theme, size.Width, size.Height, mode, editing, passed = true });
        }
        details.IsExpanded = false;
        File.WriteAllText(Path.Combine(output, "studio-layout-results.json"), JsonSerializer.Serialize(checks));
        Console.WriteLine($"PASS {checks.Count} studio editor/details layout cases.");
    }
    private static void Invoke(object value, string name) => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, null);
    private static void Layout(FrameworkElement root, Size size, double dpi)
    {
        VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
        // Bindings and visibility changes complete at render priority, even without a shown Window.
        for (var pass = 0; pass < 2; pass++)
        {
            root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        }
    }
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
