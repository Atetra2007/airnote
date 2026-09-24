using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Harmonica.Core;
using Harmonica.Desktop.Models;
using Harmonica.Desktop.Services;
using Harmonica.Windows;

namespace Harmonica.Desktop;

public partial class MainWindow : Window
{
    private readonly LibraryStore store;
    private readonly PlaybackEngine engine = new();
    private readonly DispatcherTimer parseTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly List<Border> noteTiles = [];
    private readonly Queue<string> logLines = new();
    private HotkeyManager? hotkeys;
    private HwndSource? hwndSource;
    private LibraryItem? current;
    private ScoreDocument document = new("未命名曲谱", 120, 20, "");
    private ParsedScore? parsed;
    private ScoreFormatException? scoreError;
    private bool loading, selectionGuard, dirty, busy, allowClose, closing, editMode, bpmConfirmed = true;
    private bool modalOpen;
    private int highlighted = -1;
    private CancellationTokenSource? operationCancellation;
    private CancellationTokenSource? playbackCancellation;
    private TaskCompletionSource? playbackCompletion;
    private string? lastParseMessage;
    private string? hotkeyWarning;
    private readonly string logPath;

    public MainWindow(LibraryStore store)
    {
        this.store = store;
        logPath = Path.Combine(store.Root, "desktop.log");
        InitializeComponent();
        InitializeGuide();
        parseTimer.Tick += (_, _) => { parseTimer.Stop(); ParseEditor(); };
        engine.Progress += progress => Dispatcher.BeginInvoke(() => Playback_Progress(progress));
        engine.Diagnostic += message => Dispatcher.BeginInvoke(() => Log(message));
        engine.Volume = store.Settings.Volume;
        VolumeSlider.Value = store.Settings.Volume * 100;
        SystemEvents.UserPreferenceChanged += SystemTheme_Changed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hotkeys = new HotkeyManager(new WindowInteropHelper(this).Handle);
        hotkeys.StartRequested += () => Dispatcher.BeginInvoke(Play);
        hotkeys.StopRequested += () => Dispatcher.BeginInvoke(async () => await Stop());
        hotkeys.OverlayRequested += () => Dispatcher.BeginInvoke(ToggleGuideOverlay);
        hwndSource?.AddHook(WindowMessage);
        RegisterHotkeys();
        RefreshLibrary();
        var initial = store.Items.FirstOrDefault(item => item.Id == store.Settings.LastSelectedId && item.Available)
            ?? store.Items.FirstOrDefault(item => item.Example && item.Available);
        if (initial != null) await SelectItem(initial);
        foreach (var warning in store.Warnings) Log(warning);
        if (store.Warnings.Count > 0) Status(string.Join(" ", store.Warnings), true);
        if (hotkeyWarning != null) Status(hotkeyWarning, true);
    }
    private void Window_ContentRendered(object sender, EventArgs e) => Log($"READY v0.3.0-preview.2 · {App.StartupTimer.ElapsedMilliseconds} ms（程序启动至首次呈现）");
    private nint WindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        noteInput?.HandleMessage(msg, wParam, lParam);
        if (hotkeys?.HandleMessage(msg, wParam, lParam) == true) handled = true;
        return 0;
    }
    private void RegisterHotkeys()
    {
        if (hotkeys == null) return;
        hotkeyWarning = null;
        if (!hotkeys.TryRegister(store.Settings.StartHotkey, store.Settings.StopHotkey, out var error))
        { hotkeyWarning = error; Log(error); }
        if (!hotkeys.TryRegisterOverlay(store.Settings.OverlayHotkey, store.Settings.StartHotkey, store.Settings.StopHotkey, out var overlayError))
        { hotkeyWarning ??= overlayError; Log(overlayError); }
        StopButton.ToolTip = "停止快捷键：" + store.Settings.StopHotkey;
        PlayButton.ToolTip = "开始快捷键：" + store.Settings.StartHotkey;
        UpdateAvailability();
    }
    private void SystemTheme_Changed(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (store.Settings.Theme == "System") ThemeService.Apply("System");
    });

    private void RefreshLibrary()
    {
        if (SongList == null) return;
        var query = SearchInput.Text.Trim();
        var items = store.Items.Where(item => item.Matches(query));
        if (FavoriteFilter.IsChecked == true) items = items.Where(item => item.Favorite);
        if (RecentFilter.IsChecked == true) items = items.Where(item => item.LastPlayed != null).OrderByDescending(item => item.LastPlayed);
        else items = items.OrderByDescending(item => item.Example).ThenByDescending(item => item.Available);
        var filtered = items.ToList();
        selectionGuard = true;
        SongList.ItemsSource = filtered;
        SongList.SelectedItem = filtered.Contains(current!) ? current : null;
        selectionGuard = false;
        EmptyLibrary.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryCount.Text = $"{store.Items.Count(item => item.Available)} 首本地 · {store.Items.Count} 首全部";
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) RefreshLibrary(); }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RefreshLibrary(); }
    private async void SongList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectionGuard || SongList.SelectedItem is not LibraryItem item || item == current) return;
        if (closing || busy || !await ConfirmUnsaved()) { RefreshLibrary(); return; }
        await Stop();
        await SelectItem(item);
    }
    private async Task SelectItem(LibraryItem item)
    {
        var wasBusy = busy;
        busy = true; UpdateAvailability();
        try
        {
            var next = item.Available ? await store.ReadDocumentAsync(item) : new ScoreDocument(item.Title, item.Bpm, 20, "");
            InvalidateGuideDocument();
            current = item;
            document = next;
            bpmConfirmed = !next.NeedsBpmConfirmation;
            loading = true;
            TitleInput.Text = next.Title;
            BpmInput.Text = next.Bpm.ToString(CultureInfo.InvariantCulture);
            BodyInput.Text = next.Body;
            loading = false;
            dirty = false;
            editMode = false;
            SetEditorVisibility();
            DownloadPanel.Visibility = item.Available ? Visibility.Collapsed : Visibility.Visible;
            ScoreView.Visibility = item.Available ? Visibility.Visible : Visibility.Collapsed;
            BodyInput.Visibility = Visibility.Collapsed;
            var source = item.SourceId != null && store.Catalog.TryGetValue(item.SourceId, out var catalogSong) ? catalogSong : null;
            SourceText.Text = source?.Description ?? (item.Example ? "本项目原创测试谱，可分享。" : "个人本地曲谱，不会上传。");
            ReferenceButton.Visibility = source?.EvidenceUrl != null ? Visibility.Visible : Visibility.Collapsed;
            store.Settings.LastSelectedId = item.Id;
            await store.SaveSettingsAsync();
            ParseEditor();
            RefreshLibrary();
            if (!item.Available) Status("点击“下载曲谱”从作者来源获取，或导入已有 TXT。");
            else if (!bpmConfirmed) Status("文件未注明 BPM。请修改速度，或开始时确认当前速度。", true);
            else Status("本地试听使用合成音；来源与校对记录可在下方展开查看。");
        }
        catch (Exception ex) { loading = false; ShowError(ex); RefreshLibrary(); }
        finally { busy = wasBusy; UpdateAvailability(); }
    }
    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (closing || (sender as Button)?.Tag is not LibraryItem item) return;
        item.Favorite = !item.Favorite; item.Refresh();
        try { await store.SaveLibraryAsync(); RefreshLibrary(); } catch (Exception ex) { ShowError(ex); }
    }

    private ScoreDocument ReadEditor()
    {
        if (!int.TryParse(BpmInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bpm)) throw new FormatException("BPM 必须是 20–300 的整数。");
        return document with { Title = string.IsNullOrWhiteSpace(TitleInput.Text) ? "未命名曲谱" : TitleInput.Text.Trim(), Bpm = bpm, Body = BodyInput.Text, NeedsBpmConfirmation = !bpmConfirmed };
    }
    private void Editor_Changed(object sender, TextChangedEventArgs e)
    {
        if (loading || !IsLoaded) return;
        if (sender == BpmInput) bpmConfirmed = true;
        dirty = true;
        DirtyText.Visibility = Visibility.Visible;
        parseTimer.Stop(); parseTimer.Start();
    }
    private bool ParseEditor()
    {
        parseTimer.Stop(); scoreError = null; parsed = null; lastParseMessage = null;
        LocateErrorButton.Visibility = Visibility.Collapsed;
        if (current is { Available: false }) { InvalidateGuideDocument(); NotesPanel.Children.Clear(); noteTiles.Clear(); MetadataText.Text = $"{current.Bpm} BPM  ·  {current.EditionLabel}  ·  尚未下载"; TimeText.Text = "00:00 / " + (current.DurationSeconds is double seconds ? FormatTime(seconds) : "00:00"); ProgressBar.Value = 0; UpdateAvailability(); return false; }
        try
        {
            var candidate = ReadEditor();
            parsed = ScoreCodec.Parse(candidate);
            MetadataText.Text = $"{parsed.Events.Count} 个音 / 休止  ·  {parsed.TotalBeats:0.###} 拍  ·  {FormatTime(parsed.DurationSeconds)}";
            TimeText.Text = "00:00 / " + FormatTime(parsed.DurationSeconds);
            if (!engine.IsPlaying) ProgressBar.Value = 0;
            RenderNotes();
            EnsureGuideDocument();
            DirtyText.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            UpdateAvailability();
            return true;
        }
        catch (Exception ex)
        {
            InvalidateGuideDocument();
            NotesPanel.Children.Clear(); noteTiles.Clear(); highlighted = -1;
            scoreError = ex as ScoreFormatException;
            lastParseMessage = ex.Message;
            MetadataText.Text = "曲谱需要修正";
            Status(ex.Message, true);
            LocateErrorButton.Visibility = scoreError != null ? Visibility.Visible : Visibility.Collapsed;
            UpdateAvailability();
            return false;
        }
    }
    private void RenderNotes()
    {
        NotesPanel.Children.Clear(); noteTiles.Clear(); highlighted = -1; shownGuideEvent = -1;
        if (parsed == null) return;
        foreach (var note in parsed.Events.Take(1000))
        {
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var pitch = new TextBlock { Text = note.Label, FontSize = 19, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center };
            pitch.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            var duration = new TextBlock { Text = $"{note.Beats:0.###} 拍", FontSize = 9, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            duration.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            content.Children.Add(pitch); content.Children.Add(duration);
            var tile = new Border { Child = content, MinWidth = 47, Height = 62, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 5, 5), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, ToolTip = $"{note.Label} · {note.Beats:0.###} 拍" };
            var tileIndex = noteTiles.Count;
            tile.ToolTip = $"第 {tileIndex + 1} 音 · {note.Label} · {note.Beats:0.###} 拍";
            if (InGuideMode && tileIndex == selectedGuideEvent)
            { tile.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); tile.BorderThickness = new Thickness(1); }
            tile.MouseLeftButtonDown += (_, _) => SelectGuideTile(tileIndex);
            noteTiles.Add(tile); NotesPanel.Children.Add(tile);
        }
        ScoreHint.Text = parsed.Events.Count > 1000 ? "预览前 1000 音；编辑器可查看完整谱面" : "高低音与时值 · 播放时跟随高亮";
    }
    private void SetEditorVisibility()
    {
        BodyInput.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
        ScoreView.Visibility = editMode ? Visibility.Collapsed : Visibility.Visible;
        EditButton.Content = editMode ? "预览" : "编辑";
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (busy || engine.IsPlaying || GuideActive || current is { Available: false }) return;
        editMode = !editMode; SetEditorVisibility();
        if (editMode) BodyInput.Focus(); else ParseEditor();
    }
    private void LocateError_Click(object sender, RoutedEventArgs e)
    {
        if (scoreError == null) return;
        editMode = true; SetEditorVisibility();
        BodyInput.Focus(); BodyInput.Select(Math.Clamp(scoreError.Offset, 0, BodyInput.Text.Length), scoreError.Offset < BodyInput.Text.Length ? 1 : 0);
        BodyInput.ScrollToLine(Math.Max(0, BodyInput.GetLineIndexFromCharacterIndex(Math.Clamp(scoreError.Offset, 0, BodyInput.Text.Length))));
    }
    private async Task<bool> SaveCurrent(bool copy = false)
    {
        if (busy || engine.IsPlaying || GuideActive || !ParseEditor()) return false;
        var target = current;
        busy = true; UpdateAvailability();
        try
        {
            var doc = ReadEditor();
            if (!await ConfirmBpm()) return false;
            doc = doc with { NeedsBpmConfirmation = false };
            if (target == null || copy) current = await store.SaveCopyAsync(doc);
            else await store.SaveDocumentAsync(target, doc);
            document = doc; dirty = false; current?.Refresh();
            DirtyText.Visibility = Visibility.Collapsed;
            RefreshLibrary(); Status(copy ? "已保存为独立副本，原谱保留。" : "已保存到个人曲库。", false);
            return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
        finally { busy = false; UpdateAvailability(); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (!closing) await SaveCurrent(); }
    private async void SaveCopy_Click(object sender, RoutedEventArgs e) { if (!closing) await SaveCurrent(true); }
    private async void Save_Executed(object sender, ExecutedRoutedEventArgs e) { if (!closing) await SaveCurrent(); }
    private async Task<bool> ConfirmUnsaved()
    {
        if (!dirty) return true;
        var choice = WithModal(() => MessageBox.Show(this, "当前曲谱有未保存的修改。是否保存？\n选择“取消”将保留当前编辑。", "曲谱尚未保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Question));
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && await SaveCurrent();
    }
    private T WithModal<T>(Func<T> action)
    {
        modalOpen = true;
        try { return action(); } finally { modalOpen = false; }
    }
    private Task<bool> ConfirmBpm()
    {
        if (bpmConfirmed) return Task.FromResult(true);
        var accepted = WithModal(() => MessageBox.Show(this, $"这份曲谱没有注明速度。按 {BpmInput.Text} BPM 使用吗？\n不确定时请选择“否”，核对速度后再开始。", "确认曲谱速度", MessageBoxButton.YesNo, MessageBoxImage.Question)) == MessageBoxResult.Yes;
        bpmConfirmed = accepted;
        return Task.FromResult(accepted);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!await PrepareImport()) return;
        var dialog = new OpenFileDialog { Title = "导入 TXT 曲谱", Filter = "TXT 曲谱|*.txt", Multiselect = true };
        if (WithModal(() => dialog.ShowDialog(this)) == true) await ImportFiles(dialog.FileNames);
    }
    private async void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!await PrepareImport()) return;
        var dialog = new OpenFolderDialog { Title = "选择曲谱文件夹或旧使用包目录" };
        if (WithModal(() => dialog.ShowDialog(this)) == true)
            try { await ImportFiles(LibraryStore.EnumerateImportFiles(dialog.FolderName)); } catch (Exception ex) { ShowError(ex); }
    }
    private async Task<bool> PrepareImport()
    {
        if (closing || busy) return false;
        await Stop();
        return await ConfirmUnsaved();
    }
    private async Task ImportFiles(IEnumerable<string> paths)
    {
        busy = true; UpdateAvailability();
        var imported = 0; var errors = new List<string>(); LibraryItem? last = null;
        try
        {
            foreach (var path in paths)
            {
                try { last = await store.ImportAsync(path); imported++; }
                catch (Exception ex) { errors.Add(Path.GetFileName(path) + "：" + ex.Message); }
            }
            if (last != null) await SelectItem(last);
            foreach (var error in errors) Log(error);
            Status($"已导入 / 复用 {imported} 首" + (errors.Count > 0 ? $"，{errors.Count} 项失败。{errors[0]}" : "。原文件保留，同名曲谱不会覆盖。"), errors.Count > 0);
            RefreshLibrary();
        }
        finally { busy = false; UpdateAvailability(); }
    }
    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!await PrepareImport()) return;
        var dialog = new PasteDialog { Owner = this };
        if (WithModal(() => dialog.ShowDialog()) != true) return;
        busy = true; UpdateAvailability();
        try { var item = await store.ImportTextAsync(dialog.ScoreText); await SelectItem(item); Status("粘贴曲谱已加入个人曲库。"); }
        catch (Exception ex) { ShowError(ex); }
        finally { busy = false; UpdateAvailability(); }
    }
    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (!await PrepareImport()) return;
        InvalidateGuideDocument();
        current = null; document = new ScoreDocument("未命名曲谱", 120, 20, "1 2 3 0 |", NeedsBpmConfirmation: true);
        SourceText.Text = "个人本地曲谱，不会上传。";
        ReferenceButton.Visibility = Visibility.Collapsed;
        loading = true; TitleInput.Text = document.Title; BpmInput.Text = "120"; BodyInput.Text = document.Body; loading = false;
        dirty = true; bpmConfirmed = false; editMode = true; DownloadPanel.Visibility = Visibility.Collapsed; SetEditorVisibility(); ParseEditor(); RefreshLibrary(); BodyInput.Focus();
        Status("输入你的谱面并保存。默认速度 120 BPM，请按来源调整。");
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (busy || engine.IsPlaying || GuideActive) return;
        if (sender is Button button && button.ContextMenu != null) { button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true; }
    }
    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || !await PrepareImport()) return;
        try
        {
            var paths = files.SelectMany(path => Directory.Exists(path) ? LibraryStore.EnumerateImportFiles(path) : new[] { path }).ToArray();
            if (paths.Length > 500) throw new InvalidDataException("一次最多导入 500 首。");
            if (paths.Any(path => !string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("请拖入 TXT 曲谱或曲谱文件夹。");
            await ImportFiles(paths);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (closing || current == null || busy) return;
        var item = current; busy = true; DownloadButton.IsEnabled = false; UpdateAvailability();
        operationCancellation = new CancellationTokenSource();
        Status("正在下载并核对曲谱来源…");
        try { await store.DownloadAsync(item, operationCancellation.Token); await SelectItem(item); Status("曲谱已下载并校验，可以试听；下次断网也可打开。"); }
        catch (OperationCanceledException) { Status("下载已取消。"); }
        catch (Exception ex) { ShowError(ex); }
        finally { operationCancellation.Dispose(); operationCancellation = null; busy = false; DownloadButton.IsEnabled = true; RefreshLibrary(); UpdateAvailability(); }
    }
    private void Source_Click(object sender, RoutedEventArgs e)
    {
        if (current?.SourceId is not string id || !store.Catalog.TryGetValue(id, out var source)) { Status("这份曲谱保存在本地；原创示例随本项目 MIT 许可分享。"); return; }
        if (Uri.TryCreate(source.SourceUrl, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "github.com")
            try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); }
    }
    private void Reference_Click(object sender, RoutedEventArgs e)
    {
        if (current?.SourceId is not string id || !store.Catalog.TryGetValue(id, out var song)) return;
        if (Uri.TryCreate(song.EvidenceUrl, UriKind.Absolute, out var url) && url.Scheme == "https" &&
            url.Host is "www.bilibili.com" or "www.douyin.com" or "shushu.fan")
            try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (InGuideMode) { EnsureGuideDocument(); overlayWanted = true; guideFocusTimer.Start(); UpdateOverlayVisibility(); }
        else { StopGuide(); guideFocusTimer.Stop(); guideOverlay?.SetEditable(false); guideOverlay?.Hide(); }
        ModeHint.Text = InGuideMode ? (hotkeys?.OverlayRegistered == true ? "手动弹奏 · 浮窗引导与等键练习" : "浮窗热键不可用 · 可用上方浮窗按钮") : GameMode.IsChecked == true ? "切到游戏后开始 · 自动输入有封禁风险" : "C 调合成音 · 用于核对旋律";
        if (InGuideMode && hotkeyWarning != null) Status(hotkeyWarning, true);
        UpdateAvailability();
    }
    private async void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || closing) return;
        engine.Volume = (float)(VolumeSlider.Value / 100);
        store.Settings.Volume = engine.Volume;
        try { await store.SaveSettingsAsync(); } catch (Exception ex) { ShowError(ex); }
    }
    private void Play_Click(object sender, RoutedEventArgs e) => Play();
    private async void Play()
    {
        if (InGuideMode)
        {
            if (modalOpen || closing || busy || engine.IsPlaying || engine.NeedsCleanup) return;
            if (!GuideActive && (!ParseEditor() || parsed == null || !await ConfirmBpm())) return;
            StartOrPauseGuide();
            return;
        }
        if (modalOpen || closing || busy || engine.IsPlaying || !ParseEditor() || parsed == null || !await ConfirmBpm()) return;
        if (GameMode.IsChecked == true && (hotkeys?.StartRegistered != true || hotkeys.StopRegistered != true)) { Status("热键不可用。请在设置中修改后再进行游戏演奏。", true); return; }
        var score = parsed;
        playbackCancellation = new CancellationTokenSource();
        playbackCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        busy = true; UpdateAvailability();
        try
        {
            if (current != null) { current.LastPlayed = DateTimeOffset.Now; await store.SaveLibraryAsync(); }
            var mode = GameMode.IsChecked == true ? PlaybackMode.Game : PlaybackMode.Preview;
            playbackCancellation.Token.ThrowIfCancellationRequested();
            var playing = engine.PlayAsync(score, mode, playbackCancellation.Token, store.Settings.GameNoteGapMs);
            UpdateAvailability();
            await playing;
        }
        catch (OperationCanceledException) { Status("已停止。"); }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            busy = false; ClearHighlight(); UpdateAvailability(); RefreshLibrary();
            playbackCancellation.Dispose(); playbackCancellation = null;
            playbackCompletion.TrySetResult(); playbackCompletion = null;
        }
    }
    private async Task Stop()
    {
        StopGuide();
        operationCancellation?.Cancel();
        playbackCancellation?.Cancel();
        var pending = playbackCompletion?.Task;
        try { await engine.StopAsync(); } catch (Exception ex) { ShowError(ex); }
        finally { if (pending != null) await pending; UpdateAvailability(); }
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await Stop();
    private void Playback_Progress(PlaybackProgress progress)
    {
        if (allowClose) return;
        if (progress.State == "Playing")
        {
            Status($"正在{(GameMode.IsChecked == true ? "演奏" : "试听")}  ·  {Math.Clamp(progress.EventIndex + 1, 0, progress.EventCount)} / {progress.EventCount}");
            if (highlighted != progress.EventIndex)
            {
                ClearHighlight(); highlighted = progress.EventIndex;
                if (highlighted >= 0 && highlighted < noteTiles.Count)
                {
                    noteTiles[highlighted].SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                    noteTiles[highlighted].BringIntoView();
                }
            }
        }
        else if (progress.State == "Countdown") Status("即将开始游戏演奏，请切到口琴界面。按停止键可取消倒计时。");
        else if (progress.State == "Completed") Status("播放完成。");
        else if (progress.State == "Stopped") Status("已停止。");
        ProgressBar.Value = progress.TotalSeconds > 0 ? Math.Clamp(progress.ElapsedSeconds / progress.TotalSeconds, 0, 1) : 0;
        TimeText.Text = FormatTime(progress.ElapsedSeconds) + " / " + FormatTime(progress.TotalSeconds);
    }
    private void ClearHighlight()
    {
        if (highlighted >= 0 && highlighted < noteTiles.Count) noteTiles[highlighted].Background = Brushes.Transparent;
        highlighted = -1;
    }
    private void UpdateAvailability()
    {
        if (PlayButton == null) return;
        bool occupied = closing || busy || engine.IsPlaying || GuideActive;
        PlayButton.IsEnabled = !occupied && !engine.NeedsCleanup && parsed != null && (GameMode.IsChecked != true || hotkeys?.StartRegistered == true && hotkeys.StopRegistered);
        StopButton.IsEnabled = occupied || engine.NeedsCleanup;
        TitleInput.IsEnabled = !occupied && current is not { Available: false };
        BpmInput.IsEnabled = !occupied && current is not { Available: false };
        BodyInput.IsReadOnly = occupied;
        EditButton.IsEnabled = !occupied && current is not { Available: false };
        SaveButton.IsEnabled = !occupied && parsed != null;
        ImportButton.IsEnabled = !occupied;
        PreviewMode.IsEnabled = !occupied; GameMode.IsEnabled = !occupied;
        UpdateGuideAvailability(occupied);
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (closing || busy || engine.IsPlaying || GuideActive) { Status("请先停止播放或暂停跟谱，再修改设置。"); return; }
        var dialog = new SettingsDialog(store.Settings, document.GapMs, store.Root) { Owner = this };
        if (WithModal(() => dialog.ShowDialog()) != true) return;
        _ = ApplySettings(dialog);
    }
    private async Task ApplySettings(SettingsDialog dialog)
    {
        busy = true; UpdateAvailability();
        try
        {
            if (hotkeys != null && !hotkeys.TryRegister(dialog.StartHotkey, dialog.StopHotkey, out var error))
            {
                RegisterHotkeys();
                Status("快捷键未应用：" + error, true);
                return;
            }
            store.Settings.Theme = dialog.ThemeChoice;
            store.Settings.StartHotkey = dialog.StartHotkey;
            store.Settings.StopHotkey = dialog.StopHotkey;
            store.Settings.OverlayHotkey = dialog.OverlayHotkey;
            store.Settings.GameNoteGapMs = dialog.GameNoteGapMs;
            ThemeService.Apply(dialog.ThemeChoice);
            if (document.GapMs != dialog.GapMs) { document = document with { GapMs = dialog.GapMs }; dirty = true; }
            await store.SaveSettingsAsync();
            ParseEditor(); RegisterHotkeys(); Status(hotkeyWarning ?? "设置已保存。", hotkeyWarning != null);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { busy = false; UpdateAvailability(); }
    }
    private void Details_Expanded(object sender, RoutedEventArgs e) => LogBox.ScrollToEnd();
    private void Status(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "InkBrush");
    }
    private void Log(string text)
    {
        var line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        logLines.Enqueue(line); while (logLines.Count > 200) logLines.Dequeue();
        LogBox.Text = string.Join(Environment.NewLine, logLines);
        if (DetailsExpander.IsExpanded) LogBox.ScrollToEnd();
        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 2_000_000) File.Move(logPath, logPath + ".previous", true);
            File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (IOException) { /* Logging must not interrupt stopping playback. */ }
        catch (UnauthorizedAccessException) { }
    }
    private void ShowError(Exception ex) { Status(ex.Message, true); Log(ex.Message); }
    private static string FormatTime(double seconds) => $"{(int)Math.Max(0, seconds) / 60:00}:{(int)Math.Max(0, seconds) % 60:00}";
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        UpdateAvailability();
        await Stop();
        if (engine.NeedsCleanup) { closing = false; UpdateAvailability(); Status("释放尚未完成，请点击停止重试后再关闭。", true); return; }
        if (busy || operationCancellation != null) { closing = false; UpdateAvailability(); Status("正在完成保存或导入，请完成后再次关闭。"); return; }
        if (!await ConfirmUnsaved()) { closing = false; UpdateAvailability(); return; }
        try
        {
            CloseGuide();
            await store.SaveSettingsAsync();
            await store.SaveLibraryAsync();
            await engine.DisposeAsync();
            hotkeys?.Dispose(); hwndSource?.RemoveHook(WindowMessage);
            parseTimer.Stop(); SystemEvents.UserPreferenceChanged -= SystemTheme_Changed;
            allowClose = true; _ = Dispatcher.BeginInvoke(Close);
        }
        catch (Exception ex) { closing = false; UpdateAvailability(); ShowError(ex); }
    }
}
