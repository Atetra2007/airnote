using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Harmonica.Desktop;

public partial class MainWindow
{
    private bool scoreWorkspaceOpen, workspaceUpdating;
    private const int NotesPerPage = 240;
    private int notePageStart;
    private double PlaybackSpeed => GuideSpeeds[Math.Clamp(GuideSpeed.SelectedIndex, 0, 4)];

    private void Workspace_Changed(object sender, RoutedEventArgs e)
    {
        if (LibraryWorkspace == null || workspaceUpdating) return;
        ShowScoreWorkspace(ScoreTab.IsChecked == true);
    }

    private void ShowScoreWorkspace(bool showScore)
    {
        scoreWorkspaceOpen = showScore;
        workspaceUpdating = true;
        LibraryTab.IsChecked = !showScore;
        ScoreTab.IsChecked = showScore;
        workspaceUpdating = false;
        LibraryWorkspace.Visibility = showScore ? Visibility.Collapsed : Visibility.Visible;
        ScoreWorkspace.Visibility = showScore ? Visibility.Visible : Visibility.Collapsed;
        if (showScore && highlighted >= 0) HighlightNote(highlighted);
    }

    private void OpenScore_Click(object sender, RoutedEventArgs e) => ShowScoreWorkspace(true);
    private void BackToLibrary_Click(object sender, RoutedEventArgs e) => ShowScoreWorkspace(false);

    private void ModeChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (PlaybackModePicker == null || workspaceUpdating || sender is not RadioButton button) return;
        PlaybackModePicker.SelectedIndex = int.Parse((string)button.Tag, CultureInfo.InvariantCulture);
    }

    private void PlaybackSpeed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GuideSpeed == null || guideControlsLoading || busy || engine.IsPlaying || GuideActive) return;
        guide?.SetSpeed(PlaybackSpeed);
        // A playback preference is never a document edit, even for a score with unknown BPM.
        if (IsLoaded && InGuideMode)
        {
            store.Settings.Guide.Speed = PlaybackSpeed;
            QueueGuideSettingsSave();
        }
        if (TempoSummary == null) return;
        if (InGuideMode && guide != null) PresentGuide(true);
        else if (parsed != null) TimeText.Text = "00:00 / " + FormatTime(parsed.DurationSeconds / PlaybackSpeed);
        UpdateWorkspacePresentation();
    }

    private void UpdateWorkspacePresentation()
    {
        if (PreviewControls == null) return;
        var occupied = closing || busy || engine.IsPlaying || GuideActive;
        workspaceUpdating = true;
        PreviewModeButton.IsChecked = !InGuideMode && !InGameMode;
        GuideModeButton.IsChecked = InGuideMode;
        GameModeButton.IsChecked = InGameMode;
        workspaceUpdating = false;
        PreviewModeButton.IsEnabled = GuideModeButton.IsEnabled = GameModeButton.IsEnabled = !occupied;
        PreviewControls.Visibility = !InGameMode && !InGuideMode ? Visibility.Visible : Visibility.Collapsed;
        GameControls.Visibility = InGameMode ? Visibility.Visible : Visibility.Collapsed;
        GuideRangeControls.Visibility = InGuideMode ? Visibility.Visible : Visibility.Collapsed;
        GuideSpeed.IsEnabled = !occupied && parsed != null;
        SelectedSongAuthor.Text = string.IsNullOrWhiteSpace(current?.SourceAuthor) ? "个人曲谱" : current.SourceAuthor;
        SingleVariantText.Text = current?.VariantDisplay ?? "尚未选择版本";
        SingleVariantText.Visibility = VariantPicker.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        AvailabilityText.Text = current == null ? (parsed == null ? "选择曲目或导入 TXT" : "未保存的个人曲谱")
            : current.Available ? "本地可用 · " + current.RhythmLabel
            : current.Acquisition == "external-import" ? "此版本需要导入 TXT" : "此版本尚未下载";
        TempoSummary.Text = bpmConfirmed ? $"原谱 {BpmInput.Text} BPM · 播放 {PlaybackSpeed:0.##}×" : "原谱速度待确认 · 开始前需核对 BPM";
        TransportModeText.Text = InGuideMode ? "跟谱手弹" : InGameMode ? "自动演奏" : "本地试听";
        TransportModeText.Text += $" · {PlaybackSpeed:0.##}×";
        ShortcutText.Text = $"{store.Settings.StartHotkey} {(InGuideMode ? "开始 / 暂停" : "开始")} · {store.Settings.StopHotkey} 停止";
        StopButton.Content = "停止 · " + store.Settings.StopHotkey;
        if (!InGuideMode)
        {
            ModeHint.Text = InGameMode ? "切到游戏口琴界面后开始 · 自动输入有封禁风险" : "C 调合成音 · 用于核对旋律";
            PlayButton.Content = engine.IsPlaying || playbackCompletion != null ? "正在播放" : InGameMode ? "▶  自动演奏" : "▶  试听";
        }
        if (current is { Available: false })
        {
            PlayButton.Content = current.Acquisition == "external-import" ? "导入 TXT" : "下载曲谱";
            PlayButton.IsEnabled = !occupied;
        }
        BpmInput.IsReadOnly = !editMode;
        OriginalTempoControls.Visibility = editMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowNotePage(int eventIndex)
    {
        if (parsed == null || parsed.Events.Count == 0) return;
        var start = Math.Clamp(eventIndex, 0, parsed.Events.Count - 1) / NotesPerPage * NotesPerPage;
        if (notePageStart == start && noteTiles.Count > 0) return;
        notePageStart = start;
        RenderNotes();
        ScoreScroll.ScrollToTop();
    }

    private void PreviousNotes_Click(object sender, RoutedEventArgs e) => ShowNotePage(notePageStart - NotesPerPage);
    private void NextNotes_Click(object sender, RoutedEventArgs e) => ShowNotePage(notePageStart + NotesPerPage);

    private void HighlightNote(int index)
    {
        ClearHighlight();
        if (scoreWorkspaceOpen) ShowNotePage(index);
        highlighted = index;
        var visibleIndex = index - notePageStart;
        if (visibleIndex < 0 || visibleIndex >= noteTiles.Count) return;
        noteTiles[visibleIndex].SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        if (scoreWorkspaceOpen) noteTiles[visibleIndex].BringIntoView();
    }

    private void SourceDetails_Click(object sender, RoutedEventArgs e)
    {
        var content = new DockPanel { Margin = new Thickness(24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var source = new Button { Content = "打开来源", Margin = new Thickness(0, 0, 8, 0) };
        source.Click += Source_Click; actions.Children.Add(source);
        if (ReferenceButton.Visibility == Visibility.Visible)
        {
            var reference = new Button { Content = "演奏参考", Margin = new Thickness(0, 0, 8, 0) };
            reference.Click += Reference_Click; actions.Children.Add(reference);
        }
        var close = new Button { Content = "关闭", IsCancel = true };
        actions.Children.Add(close); DockPanel.SetDock(actions, Dock.Bottom); content.Children.Add(actions);
        content.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = SourceText.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 23 } });
        var dialog = new Window { Title = "来源与校对记录", Owner = this, Width = 560, Height = 400, MinWidth = 440, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content };
        close.Click += (_, _) => dialog.Close();
        WithModal(() => dialog.ShowDialog());
    }
}
