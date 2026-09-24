using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Harmonica.Core;
using Harmonica.Windows;

namespace Harmonica.Desktop;

public partial class MainWindow
{
    private GuideEngine? guide;
    private RawNoteInput? noteInput;
    private GuideOverlayWindow? guideOverlay;
    private GuideTarget guideTarget;
    private bool guideBound, guideNeedsArm, allowCountdownSwitch, overlayWanted = true, guideControlsLoading;
    private string? guideMessage;
    private string shownGuideFeedback = "";
    private string? guideDocumentId;
    private readonly DispatcherTimer guideFrameTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly DispatcherTimer guideFocusTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer guideSettingsTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private GuidePhase? shownGuidePhase;
    private int shownGuideEvent = -1;
    private int selectedGuideEvent = -1;
    private DateTime lastGuideUi = DateTime.MinValue;
    private bool GuideActive => guide?.IsActive == true;
    private bool InGuideMode => GuideModeButton?.IsChecked == true;
    private static readonly double[] GuideSpeeds = [.5, .75, 1, 1.25, 1.5];

    private void InitializeGuide()
    {
        guidePlacedInGame = store.Settings.Guide.Left.HasValue && store.Settings.Guide.Top.HasValue;
        guideControlsLoading = true;
        GuideKind.SelectedIndex = store.Settings.Guide.Mode == "WaitForKey" ? 1 : 0;
        GuideSpeed.SelectedIndex = Array.IndexOf(GuideSpeeds, store.Settings.Guide.Speed);
        GuideOpacity.Value = store.Settings.Guide.Opacity * 100;
        GuideOpacityLabel.Text = $"{GuideOpacity.Value:0}%";
        guideControlsLoading = false;
        guideFrameTimer.Tick += (_, _) => GuideTick();
        guideFocusTimer.Tick += (_, _) => GuideFocusTick();
        guideSettingsTimer.Tick += async (_, _) =>
        {
            guideSettingsTimer.Stop();
            try { CaptureGuideBounds(); await store.SaveSettingsAsync(); }
            catch (Exception ex) { ShowError(ex); }
        };
    }
    private void EnsureGuideDocument()
    {
        if (!InGuideMode || parsed == null || closing) return;
        var previous = guide?.Timeline.Score.Document;
        if (previous != null && guideDocumentId == current?.Id && previous.Body == parsed.Document.Body && previous.Bpm == parsed.Document.Bpm &&
            previous.Title == parsed.Document.Title && previous.GapMs == parsed.Document.GapMs) return;
        StopGuide();
        guide = new GuideEngine(parsed, GuideKind.SelectedIndex == 1 ? GuideMode.WaitForKey : GuideMode.Rhythm,
            GuideSpeeds[Math.Clamp(GuideSpeed.SelectedIndex, 0, 4)]);
        guideBound = false; guideTarget = default; guideMessage = null;
        guideDocumentId = current?.Id;
        selectedGuideEvent = -1;
        guideControlsLoading = true; GuideLoop.IsChecked = false; GuideEventNumber.Text = "1"; guideControlsLoading = false;
        GuideRangeLabel.Text = $"共 {guide.Timeline.Count} 音";
        shownGuidePhase = null; shownGuideEvent = -1;
        EnsureOverlay(); guideFocusTimer.Start(); PresentGuide(true);
    }
    private void InvalidateGuideDocument()
    {
        StopGuide(); guideFocusTimer.Stop(); guide = null; guideDocumentId = null;
        guideBound = false; guideTarget = default; shownGuideEvent = -1; selectedGuideEvent = -1; shownGuidePhase = null;
        guideControlsLoading = true;
        GuideLoop.IsChecked = false; GuideEventNumber.Text = "1";
        guideControlsLoading = false;
        GuideRangeLabel.Text = "待载入曲谱";
        guideOverlay?.SetEditable(false);
        guideOverlay?.Hide();
    }
    private void EnsureOverlay()
    {
        if (guideOverlay != null) return;
        // Intentionally no Owner: minimizing the library must not minimize the independent overlay.
        guideOverlay = new GuideOverlayWindow(store.Settings.Guide);
        guideOverlay.BoundsChanged += () =>
        {
            // Preserve a deliberate adjustment even before the first game binding.
            if (guideOverlay?.Editable == true) guidePlacedInGame = true;
            QueueGuideSettingsSave();
        };
        guideOverlay.HideRequested += ToggleGuideOverlay;
        guideOverlay.Show();
    }
    private void QueueGuideSettingsSave()
    {
        if (closing || guideControlsLoading) return;
        guideSettingsTimer.Stop(); guideSettingsTimer.Start();
    }
    private void CaptureGuideBounds()
    {
        if (guideOverlay == null) return;
        var s = store.Settings.Guide;
        s.Width = guideOverlay.Width; s.Height = guideOverlay.Height;
        s.Left = guideOverlay.Left; s.Top = guideOverlay.Top;
        s.Normalize();
    }
    private async void StartOrPauseGuide()
    {
        try
        {
            if (guide?.IsActive == true) { PauseGuide(); return; }
            if (engine.IsPlaying || engine.NeedsCleanup || busy || modalOpen || closing) return;
            if (hotkeys?.StartRegistered != true || hotkeys.StopRegistered != true)
            { Status("请先在设置中解决开始 / 停止热键冲突。", true); return; }
            if (!IsActive && !GuideTarget.TryCaptureGame(out _, out _)) return;
            if (guide == null) EnsureGuideDocument();
            if (guide == null) return;
            EnsureOverlay(); overlayWanted = true; guideOverlay!.SetEditable(false);
            if (guideOverlay.InputStyleError != null) { PauseGuide(guideOverlay.InputStyleError); return; }
            guideMessage = null; guideNeedsArm = true; allowCountdownSwitch = true;
            if (guide.Phase == GuidePhase.Paused) guide.Resume(); else guide.Start();
            guideOverlay.Show(); guideFocusTimer.Start(); PresentGuide(true); SyncGuideAnimation();
            if (current != null)
            {
                current.LastPlayed = DateTimeOffset.Now;
                await store.SaveLibraryAsync(); RefreshLibrary();
            }
        }
        catch (Exception ex) { PauseGuide(ex.Message); Log(ex.Message); }
    }
    private void GuideTick()
    {
        if (guide == null || closing) { guideFrameTimer.Stop(); return; }
        try
        {
            if (guideBound && !guideTarget.IsForeground && !allowCountdownSwitch)
            { PauseGuide("已切出游戏 · 切回口琴界面后按开始继续。"); UpdateOverlayVisibility(); return; }
            // Bind/arm before advancing past the count-in. Raw input is never enabled in the count-in.
            if (guideNeedsArm && guide.Phase == GuidePhase.Countdown && guide.Snapshot.CountdownSeconds <= 0)
            {
                if (!guideBound)
                {
                    if (!GuideTarget.TryCaptureGame(out guideTarget, out var error)) { PauseGuide(error); return; }
                    guideBound = true;
                    guideOverlay?.FollowGame(guideTarget, !guidePlacedInGame);
                    guidePlacedInGame = true;
                }
                if (!guideTarget.IsForeground) { PauseGuide("游戏当前不在前台，请切回口琴界面后继续。"); return; }
                if (guide.Mode == GuideMode.WaitForKey)
                {
                    EnsureNoteInput(); noteInput!.Start(guideTarget);
                }
                guideNeedsArm = false; allowCountdownSwitch = false;
            }
            guide.Update();
            AfterGuideAdvance();
            PresentGuide(); SyncGuideAnimation();
        }
        catch (Exception ex) { PauseGuide(ex.Message); Log(ex.Message); }
    }
    private bool guidePlacedInGame;
    private void EnsureNoteInput()
    {
        if (noteInput != null) return;
        noteInput = new RawNoteInput(new WindowInteropHelper(this).Handle);
        noteInput.NotePressed += press =>
        {
            if (guide == null || !guideBound || !guideTarget.IsForeground) return;
            guide.Accept(press); AfterGuideAdvance(); PresentGuide(true); SyncGuideAnimation();
        };
        noteInput.Faulted += ex => { PauseGuide(ex.Message); Log(ex.Message); };
    }
    private void AfterGuideAdvance()
    {
        // An input callback may reach the loop boundary before the next animation tick.
        if (guide?.Phase == GuidePhase.Countdown && !guideNeedsArm)
        { StopNoteInput(); guideNeedsArm = true; allowCountdownSwitch = false; }
        if (guide?.Phase == GuidePhase.Completed) StopNoteInput();
    }
    private string? StopNoteInput()
    {
        try { noteInput?.Stop(); return null; }
        catch (Exception ex) { guideMessage = ex.Message; Log(ex.Message); return ex.Message; }
    }
    private void PauseGuide(string? reason = null)
    {
        guide?.Pause(); guideFrameTimer.Stop(); var cleanupError = StopNoteInput();
        guideNeedsArm = false; allowCountdownSwitch = false;
        guideMessage = cleanupError ?? reason;
        PresentGuide(true);
        if (guideMessage != null) Status(guideMessage, true);
    }
    private void StopGuide()
    {
        guideFrameTimer.Stop(); var cleanupError = StopNoteInput();
        guide?.Stop(); guideNeedsArm = false; guideMessage = cleanupError; allowCountdownSwitch = false;
        PresentGuide(true);
    }
    private void SyncGuideAnimation()
    {
        if (guide?.IsAnimating == true && overlayWanted) guideFrameTimer.Start();
        else guideFrameTimer.Stop();
    }
    private void GuideFocusTick()
    {
        if (closing || !InGuideMode || guide == null) { guideFocusTimer.Stop(); return; }
        if (guideBound)
        {
            if (!guideTarget.Exists)
            {
                StopGuide(); guideBound = false; guideMessage = "游戏窗口已关闭，跟谱已停止。";
                overlayWanted = false; PresentGuide(true);
            }
            else if (!guideTarget.IsForeground && GuideActive && !(guide.Phase == GuidePhase.Countdown && allowCountdownSwitch))
                PauseGuide("已切出游戏 · 切回口琴界面后按开始继续。");
            if (guideTarget.IsForeground) guideOverlay?.FollowGame(guideTarget, false);
        }
        UpdateOverlayVisibility();
        if (shownGuideFeedback != guide.Snapshot.Feedback) PresentGuide(true);
    }
    private void UpdateOverlayVisibility()
    {
        if (guideOverlay == null) return;
        bool visible = InGuideMode && overlayWanted && (guideOverlay.Editable ||
            guideBound && guideTarget.IsForeground || !guideBound && IsActive ||
            guide?.Phase == GuidePhase.Countdown && allowCountdownSwitch);
        if (visible && !guideOverlay.IsVisible) { guideOverlay.Show(); PresentGuide(true); }
        else if (!visible && guideOverlay.IsVisible) guideOverlay.Hide();
    }
    private void PresentGuide(bool force = false)
    {
        if (guide == null || !InGuideMode) return;
        var p = guide.Snapshot;
        shownGuideFeedback = p.Feedback;
        if (guideOverlay?.IsVisible == true)
            guideOverlay.Present(guide, store.Settings.StartHotkey, hotkeys?.OverlayRegistered == true ? store.Settings.OverlayHotkey : "主窗按钮", store.Settings.StopHotkey, guideMessage);
        if (!force && p.Phase == shownGuidePhase && p.EventIndex == shownGuideEvent && DateTime.UtcNow - lastGuideUi < TimeSpan.FromMilliseconds(250)) return;
        lastGuideUi = DateTime.UtcNow;
        if (p.EventIndex != shownGuideEvent)
        {
            ClearHighlight();
            highlighted = p.Phase == GuidePhase.Completed ? -1 : p.EventIndex;
            if (highlighted >= 0 && highlighted < noteTiles.Count)
            {
                noteTiles[highlighted].SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                noteTiles[highlighted].BringIntoView();
            }
            shownGuideEvent = p.EventIndex;
        }
        ProgressBar.Value = Math.Clamp(p.PositionBeats / p.TotalBeats, 0, 1);
        var secondsPerBeat = 60 / (guide.Timeline.Score.Document.Bpm * guide.Speed);
        TimeText.Text = FormatTime(p.PositionBeats * secondsPerBeat) + " / " + FormatTime(p.TotalBeats * secondsPerBeat);
        var stateText = p.Phase switch
        {
            GuidePhase.Countdown => "准备中 · 切到游戏口琴界面并松开音键",
            GuidePhase.Running => "跟谱进行中 · 音符头到线按下，尾端到线松开",
            GuidePhase.WaitingForKey => "等你弹奏 · " + GuideEngine.Describe(guide.Timeline.Score.Events[p.EventIndex]),
            GuidePhase.Paused => "已暂停 · 可定位音符、调整速度或选择 A/B 片段",
            GuidePhase.Completed => "本次练习完成。",
            _ => "跟谱手弹 · 先在口琴界面准备好，再按开始"
        };
        Status(guideMessage ?? (p.Feedback.Length > 0 ? p.Feedback : stateText), guideMessage != null);
        shownGuidePhase = p.Phase; UpdateAvailability();
    }
    private void ToggleGuideOverlay()
    {
        if (!InGuideMode || guide == null || modalOpen) return;
        if (overlayWanted) { PauseGuide(); overlayWanted = false; guideOverlay?.SetEditable(false); }
        else { overlayWanted = true; if (IsActive && !guideTarget.IsForeground) guideOverlay?.SetEditable(true); }
        UpdateOverlayVisibility(); PresentGuide(true);
    }
    private void GuideVisibility_Click(object sender, RoutedEventArgs e) => ToggleGuideOverlay();
    private void AdjustGuide_Click(object sender, RoutedEventArgs e)
    {
        if (guide == null) return;
        PauseGuide(); EnsureOverlay(); overlayWanted = true;
        guideOverlay!.SetEditable(!guideOverlay.Editable); guideOverlay.Show(); guideOverlay.EnsureVisible(); PresentGuide(true);
        Status(guideOverlay.Editable ? "浮窗已解锁：拖动标题移动，右下角缩放。切到游戏开始后自动锁定。" : "浮窗已锁定；切到游戏后按开始。");
    }
    private void GuideOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || guideControlsLoading) return;
        store.Settings.Guide.Opacity = GuideOpacity.Value / 100;
        GuideOpacityLabel.Text = $"{GuideOpacity.Value:0}%";
        guideOverlay?.SetBackgroundOpacity(store.Settings.Guide.Opacity); QueueGuideSettingsSave();
    }
    private void GuideOptions_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || guideControlsLoading || GuideActive) return;
        var mode = GuideKind.SelectedIndex == 1 ? GuideMode.WaitForKey : GuideMode.Rhythm;
        var speed = GuideSpeeds[Math.Clamp(GuideSpeed.SelectedIndex, 0, 4)];
        guide?.SetMode(mode); guide?.SetSpeed(speed);
        store.Settings.Guide.Mode = mode.ToString(); store.Settings.Guide.Speed = speed;
        guideMessage = null; PresentGuide(true); QueueGuideSettingsSave();
    }
    private bool TryGuideSelection(out int index)
    {
        index = -1;
        if (guide == null || guide.IsActive) return false;
        if (!int.TryParse(GuideEventNumber.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > guide.Timeline.Count)
        { Status($"音符编号应为 1–{guide.Timeline.Count}。", true); return false; }
        index = number - 1; return true;
    }
    private void SeekGuide_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGuideSelection(out var index)) return;
        if (index < guide!.RangeStart || index > guide.RangeEnd) { Status("所选音符不在 A/B 片段内；可先点击“全曲”。", true); return; }
        guide.SeekToEvent(index); guideMessage = null; PresentGuide(true);
    }
    private void SetGuideA_Click(object sender, RoutedEventArgs e)
    {
        if (TryGuideSelection(out var index)) SetGuideRange(index, Math.Max(index, guide!.RangeEnd));
    }
    private void SetGuideB_Click(object sender, RoutedEventArgs e)
    {
        if (TryGuideSelection(out var index)) SetGuideRange(Math.Min(guide!.RangeStart, index), index);
    }
    private void ClearGuideRange_Click(object sender, RoutedEventArgs e)
    {
        if (guide == null || guide.IsActive) return;
        guideControlsLoading = true; GuideLoop.IsChecked = false; guideControlsLoading = false;
        SetGuideRange(0, guide.Timeline.Count - 1);
    }
    private void GuideLoop_Changed(object sender, RoutedEventArgs e)
    { if (IsLoaded && !guideControlsLoading && guide != null && !guide.IsActive) SetGuideRange(guide.RangeStart, guide.RangeEnd); }
    private void SetGuideRange(int start, int end)
    {
        guide!.SetLoop(start, end, GuideLoop.IsChecked == true);
        GuideRangeLabel.Text = $"A {start + 1} → B {end + 1}";
        guideMessage = null; PresentGuide(true);
    }
    private void SelectGuideTile(int index)
    {
        if (!InGuideMode || GuideActive) return;
        GuideEventNumber.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
        Status($"已选第 {index + 1} 个音，可定位或设置 A/B。" );
    }
    private void GuideEventNumber_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || guideControlsLoading || GuideActive) return;
        if (selectedGuideEvent >= 0 && selectedGuideEvent < noteTiles.Count)
            noteTiles[selectedGuideEvent].BorderThickness = new Thickness(0);
        selectedGuideEvent = int.TryParse(GuideEventNumber.Text, out var n) ? n - 1 : -1;
        if (selectedGuideEvent >= 0 && selectedGuideEvent < noteTiles.Count)
        {
            noteTiles[selectedGuideEvent].SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            noteTiles[selectedGuideEvent].BorderThickness = new Thickness(1);
        }
    }
    private void UpdateGuideAvailability(bool occupied)
    {
        if (GuideControls == null) return;
        GuideControls.Visibility = InGuideMode ? Visibility.Visible : Visibility.Collapsed;
        GuideModeButton.IsEnabled = !occupied;
        GuideVisibilityButton.Content = overlayWanted ? "隐藏浮窗" : "显示浮窗";
        GuideVisibilityButton.IsEnabled = guide != null && !closing && !busy;
        AdjustGuideButton.Content = guideOverlay?.Editable == true ? "完成调整" : "调整浮窗";
        if (InGuideMode)
        {
            ModeHint.Text = hotkeys?.OverlayRegistered != true ? "浮窗热键不可用 · 使用上方浮窗按钮" :
                GuideKind.SelectedIndex == 1 ? "弹对才继续 · 同音请松开后再按" : "跟随节奏 · 到线按下，尾端松开";
            PlayButton.ToolTip = store.Settings.StartHotkey + " 开始 / 暂停 / 继续";
            PlayButton.Content = GuideActive ? "Ⅱ  暂停" : guide?.Phase == GuidePhase.Paused ? "▶  继续" : "▶  开始";
            PlayButton.IsEnabled = !closing && !busy && !engine.IsPlaying && !engine.NeedsCleanup && parsed != null && hotkeys?.StartRegistered == true && hotkeys.StopRegistered;
            StopButton.IsEnabled = GuideActive || guide?.Phase is GuidePhase.Paused or GuidePhase.Completed;
        }
        else { PlayButton.Content = "▶  开始"; PlayButton.ToolTip = "开始快捷键：" + store.Settings.StartHotkey; }
        bool canAdjust = !occupied && guide != null;
        GuideKind.IsEnabled = GuideSpeed.IsEnabled = GuideEventNumber.IsEnabled = SeekGuideButton.IsEnabled =
            SetGuideAButton.IsEnabled = SetGuideBButton.IsEnabled = GuideLoop.IsEnabled = ClearGuideRangeButton.IsEnabled = canAdjust;
        AdjustGuideButton.IsEnabled = guide != null && !closing && !busy;
    }
    private void CloseGuide()
    {
        StopGuide(); guideFocusTimer.Stop(); guideSettingsTimer.Stop();
        CaptureGuideBounds(); noteInput?.Dispose(); noteInput = null;
        guideOverlay?.CloseForExit(); guideOverlay = null; guide = null;
    }
}
