using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Harmonica.Desktop.Models;

namespace Harmonica.Desktop;

public partial class MainWindow
{
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private bool libraryFilterGuard, librarySelectionPending, focusLibraryAfterSelection;

    private void InitializeLibrary()
    {
        // The library is now the main workspace. Keep the old sidebar preference
        // on disk for compatibility, without applying its narrow-list geometry.
        LibraryColumn.Width = new GridLength(1, GridUnitType.Star);
        DetailColumn.Width = new GridLength(320);
        SortPicker.SelectedIndex = (int)store.Settings.LibrarySort;
        searchTimer.Tick += (_, _) => ApplyLibraryFilters();
    }

    private LibraryView SelectedLibraryView() => LocalFilter.IsChecked == true ? LibraryView.Local
        : FavoriteFilter.IsChecked == true ? LibraryView.Favorites
        : RecentFilter.IsChecked == true ? LibraryView.Recent : LibraryView.All;

    private LibraryQualityFilter SelectedQualityFilter() => QualityFilter.SelectedIndex switch
    {
        1 => LibraryQualityFilter.ReviewedFragment,
        2 => LibraryQualityFilter.UnknownBpm,
        _ => LibraryQualityFilter.All
    };

    private void RefreshLibrary(bool resetScroll = false)
    {
        if (SongList == null) return;
        var works = store.GetWorks();
        var view = SelectedLibraryView();
        LibraryViewTitle.Text = view switch
        {
            LibraryView.Local => "本地曲谱",
            LibraryView.Favorites => "我的收藏",
            LibraryView.Recent => "最近使用",
            _ => "全部曲目"
        };
        var filtered = LibrarySearch.Find(works, SearchInput.Text, view, SelectedQualityFilter(),
            store.Settings.LibrarySort, current?.Id);
        var scroll = FindLibraryChild<ScrollViewer>(SongList);
        var offset = resetScroll ? 0 : scroll?.VerticalOffset ?? 0;
        selectionGuard = true;
        SongList.ItemsSource = filtered;
        SongList.SelectedItem = filtered.FirstOrDefault(work => current != null && work.Variants.Any(item => item.Id == current.Id));
        selectionGuard = false;
        SongList.UpdateLayout();
        (scroll ?? FindLibraryChild<ScrollViewer>(SongList))?.ScrollToVerticalOffset(offset);
        EmptyLibrary.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var hasQuery = !string.IsNullOrWhiteSpace(SearchInput.Text);
        var hasFilters = hasQuery || view != LibraryView.All || SelectedQualityFilter() != LibraryQualityFilter.All;
        ClearSearchButton.Visibility = SearchInput.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResetLibraryButton.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
        ResultCount.Text = $"{filtered.Count} 首{(hasFilters ? "匹配" : "作品")} · {filtered.Sum(work => work.Variants.Count)} 个版本";
        LibraryCount.Text = $"{works.Count(work => work.Variants.Any(item => item.Available))} 首本地 / 共 {works.Count} 首";
        SortPicker.IsEnabled = view != LibraryView.Recent;
        var previousFilterGuard = libraryFilterGuard;
        libraryFilterGuard = true;
        SortPicker.SelectedIndex = view == LibraryView.Recent ? (int)LibrarySortOrder.LastPlayed : (int)store.Settings.LibrarySort;
        libraryFilterGuard = previousFilterGuard;
        SortPicker.ToolTip = view == LibraryView.Recent ? "最近视图按使用时间排列" : "智能排序：搜索相关度优先；无搜索时优先本地、收藏和最近使用";
        EmptyLibraryTitle.Text = hasQuery ? "没有找到这首曲目" : view switch
        {
            LibraryView.Favorites => "还没有匹配的收藏",
            LibraryView.Recent => "还没有使用记录",
            LibraryView.Local => "没有匹配的本地曲谱",
            _ => "没有匹配的曲谱"
        };
        EmptyLibraryHelp.Text = hasQuery ? "试试更短的歌名、别名或作者；也可以重置筛选后再找。" : view switch
        {
            LibraryView.Favorites => "点击曲目右侧的 ☆，把常用曲目留在这里。",
            LibraryView.Recent => "开始试听或练习后，曲目会出现在这里。",
            LibraryView.Local => "导入自己的 TXT，或在全部曲库中获取曲谱。",
            _ => "切换到全部状态，或导入自己的 TXT。"
        };
        EmptyResetButton.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLibraryFilters()
    {
        searchTimer.Stop();
        RefreshLibrary(true);
        UpdateVariantPicker();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || libraryFilterGuard) return;
        ClearSearchButton.Visibility = SearchInput.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        searchTimer.Stop();
        searchTimer.Start();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || libraryFilterGuard) return;
        ApplyLibraryFilters();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || libraryFilterGuard) return;
        store.Settings.LibrarySort = (LibrarySortOrder)Math.Max(0, SortPicker.SelectedIndex);
        ApplyLibraryFilters();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchInput.Clear();
        ApplyLibraryFilters();
        SearchInput.Focus();
    }

    private void ResetLibrary_Click(object sender, RoutedEventArgs e)
    {
        libraryFilterGuard = true;
        SearchInput.Clear();
        AllFilter.IsChecked = true;
        QualityFilter.SelectedIndex = 0;
        libraryFilterGuard = false;
        ApplyLibraryFilters();
        SearchInput.Focus();
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ShowScoreWorkspace(false);
        SearchInput.Focus();
        SearchInput.SelectAll();
        e.Handled = true;
    }

    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // IME composition keys are intentionally left to the input method.
        if (e.Key == Key.Escape)
        {
            ClearSearch_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            ApplyLibraryFilters();
            if (SongList.Items.Count > 0 && !librarySelectionPending && (!busy || playbackCompletion != null))
            {
                focusLibraryAfterSelection = e.Key == Key.Down;
                SongList.SelectedIndex = 0;
                if (focusLibraryAfterSelection && !librarySelectionPending) FocusLibrarySelection();
            }
            e.Handled = true;
        }
    }

    private void SongList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchInput.Focus();
            SearchInput.SelectAll();
            e.Handled = true;
        }
    }

    private void FocusLibrarySelection()
    {
        focusLibraryAfterSelection = false;
        if (SongList.SelectedItem is not { } selected) return;
        SongList.ScrollIntoView(selected);
        SongList.UpdateLayout();
        (SongList.ItemContainerGenerator.ContainerFromItem(selected) as ListBoxItem)?.Focus();
    }

    private static T? FindLibraryChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindLibraryChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }
}
