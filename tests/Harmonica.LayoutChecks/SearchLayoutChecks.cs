using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Harmonica.Desktop;
using Harmonica.Desktop.Models;
using Harmonica.Desktop.Services;

internal static class SearchLayoutChecks
{
    public static void Run(string output)
    {
        // Use the shipped catalog and an isolated profile. Never show a window or register hotkeys.
        var store = new LibraryStore(Path.Combine(output, "search-profile"), Path.Combine(AppContext.BaseDirectory, "Resources"));
        store.InitializeAsync().GetAwaiter().GetResult();
        var window = new MainWindow(store);
        var root = (FrameworkElement)window.Content;
        ((Grid)root).SetResourceReference(Panel.BackgroundProperty, "CanvasBrush");
        var search = (TextBox)window.FindName("SearchInput");
        var list = (ListBox)window.FindName("SongList");
        var cases = new List<object>();
        void Refresh() => Invoke(window, "ApplyLibraryFilters");
        ((RadioButton)window.FindName("AllFilter")).IsChecked = true;
        Refresh();
        var selected = store.Items.First(item => item.Available && item.Example);
        Select(window, selected);
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var size in new[] { new Size(924, 561), new Size(1224, 781) })
        foreach (var dpi in new[] { 1d, 1.5, 2 })
        {
            ThemeService.Apply(theme);
            foreach (var query in new[] { "", "欧布小曲", "晴天 137", "不存在的曲目XYZ" })
            {
                search.Text = query;
                Refresh();
                Layout(root, size, dpi);
                var names = new List<string> { "SearchInput", "AllFilter", "LocalFilter", "FavoriteFilter", "RecentFilter", "QualityFilter", "SortPicker", "ResultCount", "ImportButton", "LibraryCount", "PlayButton", "StopButton", "SettingsButton", "LibraryViewTitle", "LibraryTab", "ScoreTab", "PreviewModeButton", "GuideModeButton", "GameModeButton" };
                if (list.Items.Count == 0) names.AddRange(["EmptyLibraryTitle", "EmptyLibraryHelp", "EmptyResetButton"]);
                CheckBounds(window, root, names, size);
                if (((FrameworkElement)window.FindName("LibraryWorkspace")).Visibility != Visibility.Visible || ((FrameworkElement)window.FindName("ScoreWorkspace")).Visibility != Visibility.Collapsed)
                    throw new Exception("Searching the library revealed the complete score");
                if (list.ActualWidth < size.Width * .45) throw new Exception("The library is still confined to a narrow sidebar");
                var resultCount = (TextBlock)window.FindName("ResultCount");
                var text = new FormattedText(resultCount.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(resultCount.FontFamily, resultCount.FontStyle, resultCount.FontWeight, resultCount.FontStretch), resultCount.FontSize, Brushes.Black, dpi);
                if (text.Width > resultCount.ActualWidth + 1) throw new Exception("Result count is clipped");
                if (query == "" && list.Items.Count != store.GetWorks().Count) throw new Exception("Full catalog is not visible");
                if (query is "欧布小曲" or "晴天 137" && list.Items.Count != 1) throw new Exception("Real catalog query did not find one work: " + query);
                if (query == "不存在的曲目XYZ" && list.Items.Count != 0) throw new Exception("Empty state has results");
                if (query == "" || dpi == 1)
                    Save(root, size, dpi, Path.Combine(output, $"library-{theme}-{size.Width}-{dpi:0.0}-{(query == "" ? "all" : query == "欧布小曲" ? "alias" : query == "晴天 137" ? "combined" : "empty")}.png"));
                cases.Add(new { theme, dpi, windowWidth = size.Width, windowHeight = size.Height, query, works = list.Items.Count });
            }
        }
        search.Text = "";
        ((RadioButton)window.FindName("LocalFilter")).IsChecked = true;
        Refresh();
        if (list.Items.Cast<LibraryWork>().Any(work => work.Variants.Any(item => !item.Available))) throw new Exception("Local tab leaked an unavailable version");
        CheckWorkspaceNavigation(window, selected);
        CheckModeLayouts(window, output);
        ((RadioButton)window.FindName("RecentFilter")).IsChecked = true;
        Refresh();
        if (((ComboBox)window.FindName("SortPicker")).IsEnabled) throw new Exception("Recent view should make its ordering explicit");
        typeof(MainWindow).GetMethod("ResetLibrary_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
        if (list.Items.Count != store.GetWorks().Count || search.Text != "" || ((RadioButton)window.FindName("AllFilter")).IsChecked != true)
            throw new Exception("Reset did not restore the full library");
        var unavailable = store.Items.First(item => !item.Available && item.Acquisition == "external-import");
        Select(window, unavailable);
        var action = (Button)window.FindName("PlayButton");
        if (!action.IsEnabled || !(action.Content?.ToString() ?? "").Contains("导入"))
            throw new Exception("An unavailable external score must offer import, not an unusable Play action");
        if (((FrameworkElement)window.FindName("ScoreWorkspace")).Visibility != Visibility.Collapsed)
            throw new Exception("Selecting a missing score must not open the full score workspace");
        if ((Harmonica.Core.ParsedScore?)typeof(MainWindow).GetField("parsed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) != null)
            throw new Exception("Unavailable score retained parsed notes from the previously selected song");
        var works = store.GetWorks();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) LibrarySearch.Find(works, "晴天 137");
        watch.Stop();
        if (window.IsLoaded || window.IsVisible) throw new Exception("Search check opened a desktop window");
        var report = new { scope = "offline catalog search and offscreen WPF layout; no desktop input or game test", works = works.Count, versions = store.Items.Count, cases, searches = 100, searchMilliseconds = watch.ElapsedMilliseconds };
        File.WriteAllText(Path.Combine(output, "search-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {cases.Count} search layout cases; {works.Count} works / {store.Items.Count} versions; 100 searches in {watch.ElapsedMilliseconds} ms.");
    }

    private static void CheckWorkspaceNavigation(MainWindow window, LibraryItem selected)
    {
        var search = (TextBox)window.FindName("SearchInput");
        var list = (ListBox)window.FindName("SongList");
        var variants = (ComboBox)window.FindName("VariantPicker");
        search.Text = selected.Title;
        Invoke(window, "ApplyLibraryFilters");
        if (list.SelectedItem is not LibraryWork before || before.SelectedVariant.Id != selected.Id)
            throw new Exception("The local selection was lost while applying its search");
        var variant = variants.SelectedItem;
        var sort = ((ComboBox)window.FindName("SortPicker")).SelectedIndex;
        var mode = ((ComboBox)window.FindName("PlaybackModePicker")).SelectedIndex;
        foreach (var showScore in new[] { true, false, true, false })
        {
            Invoke(window, "ShowScoreWorkspace", showScore);
            if ((((FrameworkElement)window.FindName("ScoreWorkspace")).Visibility == Visibility.Visible) != showScore ||
                (((FrameworkElement)window.FindName("LibraryWorkspace")).Visibility == Visibility.Visible) == showScore)
                throw new Exception("Library and score must occupy separate workspaces");
            if (search.Text != selected.Title || ((RadioButton)window.FindName("LocalFilter")).IsChecked != true ||
                ((ComboBox)window.FindName("SortPicker")).SelectedIndex != sort || ((ComboBox)window.FindName("PlaybackModePicker")).SelectedIndex != mode ||
                list.SelectedItem is not LibraryWork after || after.SelectedVariant.Id != selected.Id || !ReferenceEquals(variants.SelectedItem, variant))
                throw new Exception("Changing workspace lost the query, filters, version or current song");
            if ((bool)typeof(MainWindow).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!)
                throw new Exception("Opening the score marked the document unsaved");
        }
        search.Text = "";
        Invoke(window, "ApplyLibraryFilters");
        Console.WriteLine("PASS repeated library/score navigation preserves search, filters, song, version and clean state.");
    }

    private static void CheckModeLayouts(MainWindow window, string output)
    {
        ThemeService.Apply("Dark");
        var root = (FrameworkElement)window.Content;
        foreach (var size in new[] { new Size(924, 561), new Size(1224, 781) })
        foreach (var dpi in new[] { 1d, 1.5, 2 })
        foreach (var mode in new[] { 0, 1, 2 })
        {
            ((ComboBox)window.FindName("PlaybackModePicker")).SelectedIndex = mode;
            Invoke(window, "UpdateAvailability");
            Layout(root, size, dpi);
            CheckBounds(window, root, ["LibraryTab", "ScoreTab", "PreviewModeButton", "GuideModeButton", "GameModeButton", "PlayButton", "StopButton", "GuideSpeed"], size);
            Save(root, size, dpi, Path.Combine(output, $"library-Dark-{size.Width}-{dpi:0.0}-mode{mode}.png"));
        }
        ((ComboBox)window.FindName("PlaybackModePicker")).SelectedIndex = 0;
        Invoke(window, "UpdateAvailability");
    }

    private static object? Invoke(object target, string name, params object[] arguments)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private static void Select(MainWindow window, LibraryItem item)
    {
        // A shown WPF window installs this context through its message loop. The component harness
        // must supply it before starting async selection so file reads resume on the owning thread.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        try { Wait((Task)Invoke(window, "SelectItem", item, false)!); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void Wait(Task task)
    {
        // Pump only the isolated component's dispatcher; never create an HWND or register hotkeys.
        var deadline = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Isolated score selection did not complete");
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }

    private static void Layout(FrameworkElement root, Size size, double dpi)
    {
        VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
        for (var pass = 0; pass < 2; pass++)
        {
            root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        }
    }

    private static void CheckBounds(Window window, FrameworkElement root, IEnumerable<string> names, Size size)
    {
        foreach (var name in names)
        {
            var element = (FrameworkElement)window.FindName(name);
            var rect = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            if (element.Visibility != Visibility.Visible || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0 || rect.Right > size.Width + .1 || rect.Bottom > size.Height + .1 || rect.Left < -.1 || rect.Top < -.1)
                throw new Exception($"Library workspace clips {name} at {size}: {rect}");
        }
    }

    private static void Save(FrameworkElement root, Size size, double dpi, string path)
    {
        var bitmap = new RenderTargetBitmap((int)(size.Width * dpi), (int)(size.Height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
