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
        var sidebar = (ColumnDefinition)window.FindName("LibraryColumn");
        var cases = new List<object>();
        void Refresh() => typeof(MainWindow).GetMethod("ApplyLibraryFilters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var width in new[] { 280d, 330d, 420d })
        foreach (var size in new[] { new Size(924, 561), new Size(1144, 721) })
        {
            ThemeService.Apply(theme);
            sidebar.Width = new GridLength(width, GridUnitType.Star);
            ((ColumnDefinition)window.FindName("DetailColumn")).Width = new GridLength(1139 - width, GridUnitType.Star);
            foreach (var query in new[] { "", "欧布小曲", "晴天 137", "不存在的曲目XYZ" })
            {
                search.Text = query;
                Refresh();
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                var names = new List<string> { "SearchInput", "AllFilter", "LocalFilter", "FavoriteFilter", "RecentFilter", "QualityFilter", "SortPicker", "ResultCount", "ImportButton", "LibraryCount", "PlayButton", "StopButton", "EditButton", "TitleInput", "ScoreScroll", "PlaybackModePicker", "SettingsButton", "LibraryViewTitle" };
                if (list.Items.Count == 0) names.AddRange(["EmptyLibraryTitle", "EmptyLibraryHelp", "EmptyResetButton"]);
                foreach (var name in names)
                {
                    var element = (FrameworkElement)window.FindName(name);
                    var rect = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                    if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0 || rect.Right > size.Width + .1 || rect.Bottom > size.Height + .1 || rect.Left < -.1 || rect.Top < -.1)
                        throw new Exception($"Search layout clips {name} at {size}, sidebar {width}: {rect}");
                }
                var resultCount = (TextBlock)window.FindName("ResultCount");
                var text = new FormattedText(resultCount.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(resultCount.FontFamily, resultCount.FontStyle, resultCount.FontWeight, resultCount.FontStretch), resultCount.FontSize, Brushes.Black, 1);
                if (text.Width > resultCount.ActualWidth + 1) throw new Exception("Result count is clipped");
                if (query == "" && list.Items.Count != store.GetWorks().Count) throw new Exception("Full catalog is not visible");
                if (query is "欧布小曲" or "晴天 137" && list.Items.Count != 1) throw new Exception("Real catalog query did not find one work: " + query);
                if (query == "不存在的曲目XYZ" && list.Items.Count != 0) throw new Exception("Empty state has results");
                if (width == 330 && size.Width == 1144 || width == 420 && size.Width == 924 && query == "不存在的曲目XYZ")
                {
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"search-{theme}-{size.Width}-{width}-{(query == "" ? "all" : query == "欧布小曲" ? "alias" : query == "晴天 137" ? "combined" : "empty")}.png"));
                    encoder.Save(file);
                }
                cases.Add(new { theme, sidebarWidth = width, windowWidth = size.Width, windowHeight = size.Height, query, works = list.Items.Count });
            }
        }
        search.Text = "";
        ((RadioButton)window.FindName("LocalFilter")).IsChecked = true;
        Refresh();
        if (list.Items.Cast<LibraryWork>().Any(work => work.Variants.Any(item => !item.Available))) throw new Exception("Local tab leaked an unavailable version");
        ((RadioButton)window.FindName("RecentFilter")).IsChecked = true;
        Refresh();
        if (((ComboBox)window.FindName("SortPicker")).IsEnabled) throw new Exception("Recent view should make its ordering explicit");
        typeof(MainWindow).GetMethod("ResetLibrary_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
        if (list.Items.Count != store.GetWorks().Count || search.Text != "" || ((RadioButton)window.FindName("AllFilter")).IsChecked != true)
            throw new Exception("Reset did not restore the full library");
        var works = store.GetWorks();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) LibrarySearch.Find(works, "晴天 137");
        watch.Stop();
        if (window.IsLoaded || window.IsVisible) throw new Exception("Search check opened a desktop window");
        var report = new { scope = "offline catalog search and offscreen WPF layout; no desktop input or game test", works = works.Count, versions = store.Items.Count, cases, searches = 100, searchMilliseconds = watch.ElapsedMilliseconds };
        File.WriteAllText(Path.Combine(output, "search-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {cases.Count} search layout cases; {works.Count} works / {store.Items.Count} versions; 100 searches in {watch.ElapsedMilliseconds} ms.");
    }
}
