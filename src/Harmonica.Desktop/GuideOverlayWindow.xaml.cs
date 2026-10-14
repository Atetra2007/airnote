using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Harmonica.Desktop.Models;
using Harmonica.Windows;

namespace Harmonica.Desktop;

public partial class GuideOverlayWindow : Window
{
    private bool editable, allowClose;
    private HwndSource? source;
    private (int Left, int Top, int Width, int Height)? previousGameBounds;
    public event Action? BoundsChanged;
    public event Action? HideRequested;
    public bool Editable => editable;
    public string? InputStyleError { get; private set; }
    public GuideOverlayWindow(GuidePreferences settings)
    {
        InitializeComponent();
        Width = settings.Width; Height = settings.Height;
        Left = settings.Left ?? SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = settings.Top ?? SystemParameters.WorkArea.Top + 32;
        SetBackgroundOpacity(settings.Opacity);
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WindowMessage);
            ApplyInputStyle(); EnsureVisible();
        };
        LocationChanged += (_, _) => BoundsChanged?.Invoke();
        SizeChanged += (_, _) => BoundsChanged?.Invoke();
        Closing += (_, args) => { if (!allowClose) { args.Cancel = true; HideRequested?.Invoke(); } };
        Closed += (_, _) => source?.RemoveHook(WindowMessage);
    }
    public void SetBackgroundOpacity(double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, .35, .95) * 255), 34, 35, 38));
        brush.Freeze(); Frame.Background = brush;
    }
    public void SetEditable(bool value)
    {
        editable = value;
        // Adjustment is a normal switchable window; performance stays out of Alt+Tab/the taskbar.
        ShowInTaskbar = value;
        ResizeGrip.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        Frame.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value ? "#C9CAD2" : "#38393F"));
        ApplyInputStyle();
    }
    private void ApplyInputStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var style = OverlayNative.GetWindowLongPtr(handle, -20).ToInt64();
        style = editable ? style & ~(0x80L | 0x20L | 0x08000000L) : style | 0x80 | 0x20 | 0x08000000; // TOOLWINDOW + TRANSPARENT + NOACTIVATE
        Marshal.SetLastPInvokeError(0);
        var previous = OverlayNative.SetWindowLongPtr(handle, -20, new nint(style));
        var error = Marshal.GetLastPInvokeError();
        InputStyleError = previous == 0 && error != 0 ? "无法设置浮窗穿透：" + new Win32Exception(error).Message : null;
        if (!OverlayNative.SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x20))
            InputStyleError = "无法锁定浮窗位置：" + new Win32Exception(Marshal.GetLastPInvokeError()).Message;
    }
    public void Present(GuideEngine engine, string start, string toggle, string stop, string? message = null)
    {
        var p = engine.Snapshot;
        var score = engine.Timeline.Score;
        SongTitle.Text = score.Document.Title;
        ModeLabel.Text = (engine.Mode == GuideMode.Rhythm ? "节奏跟弹" : "等键练习") + $" · {engine.Speed:0.##}×";
        Notes.Timeline = engine.Timeline; Notes.Progress = p; Notes.Speed = engine.Speed;
        Notes.RangeStart = engine.RangeStart; Notes.RangeEnd = engine.RangeEnd; Notes.InvalidateVisual();
        CurrentKey.Text = p.Phase != GuidePhase.Completed && p.EventIndex <= engine.RangeEnd ?
            (p.Phase == GuidePhase.WaitingForKey ? "请弹  " : "当前  ") + GuideEngine.Describe(score.Events[p.EventIndex]) : "本次练习完成";
        CurrentKey.Foreground = p.Phase == GuidePhase.WaitingForKey ? WaitingInk : NormalInk;
        var next = Math.Min(p.EventIndex + 1, engine.RangeEnd + 1);
        while (next <= engine.RangeEnd && score.Events[next].Degree == 0) next++;
        NextKey.Text = next <= engine.RangeEnd ? "下一音  " + GuideEngine.Describe(score.Events[next]) : "下一音  —";
        PositionLabel.Text = $"{Math.Clamp(p.PositionBeats / p.TotalBeats * 100, 0, 100):0}%";
        ShortcutLabel.Text = editable ? "调整中 · 拖动标题移动，右下角缩放；开始后自动锁定" : $"{start} 开始 / 暂停　{toggle} 浮窗　{stop} 停止";
        StateLabel.Text = message ?? (p.Feedback.Length > 0 ? p.Feedback : p.Phase switch
        {
            GuidePhase.Countdown => $"{Math.Max(1, (int)Math.Ceiling(p.CountdownSeconds))}\n准备 · 松开音键",
            GuidePhase.WaitingForKey => "", // Keep upcoming notes unobscured; the lower row gives the full required chord.
            GuidePhase.Paused => "已暂停 · " + start + " 继续",
            GuidePhase.Stopped => "准备好后切到口琴界面 · " + start + " 开始",
            GuidePhase.Completed => "练习完成 · " + start + " 再来一次",
            _ => ""
        });
        StatePlate.Visibility = StateLabel.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private static readonly Brush WaitingInk = FrozenBrush("#C9CAD2"), NormalInk = FrozenBrush("#EEEEEF");
    private static Brush FrozenBrush(string color)
    { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    private void Header_DragDelta(object sender, DragDeltaEventArgs e)
    { if (editable) { Left += e.HorizontalChange; Top += e.VerticalChange; } }
    private void Header_DragCompleted(object sender, DragCompletedEventArgs e)
    { if (editable) EnsureVisible(); }
    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!editable) return;
        Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, 1600);
        Height = Math.Clamp(Height + e.VerticalChange, MinHeight, 1000);
        EnsureVisible();
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21 && !editable) { handled = true; return 3; } // MA_NOACTIVATE
        if (message is 0x02E0 or 0x007E) _ = Dispatcher.BeginInvoke(EnsureVisible);
        return 0;
    }
    public void FollowGame(GuideTarget target, bool center)
    {
        var bounds = target.ClientBounds();
        var handle = new WindowInteropHelper(this).Handle;
        if (bounds == null || handle == 0 || !OverlayNative.GetWindowRect(handle, out var rect)) return;
        var b = bounds.Value;
        var x = rect.Left; var y = rect.Top;
        if (center) { x = b.Left + (b.Width - (rect.Right - rect.Left)) / 2; y = b.Top + 32; }
        else if (previousGameBounds is { } previous) { x += b.Left - previous.Left; y += b.Top - previous.Top; }
        if (x != rect.Left || y != rect.Top) OverlayNative.SetWindowPos(handle, 0, x, y, 0, 0, 0x1 | 0x4 | 0x10);
        previousGameBounds = b;
        EnsureVisible();
    }
    public void EnsureVisible()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0 || !OverlayNative.GetWindowRect(handle, out var rect)) return;
        var monitor = OverlayNative.MonitorFromWindow(handle, 2);
        var info = new OverlayNative.MonitorInfo { Size = (uint)Marshal.SizeOf<OverlayNative.MonitorInfo>() };
        if (!OverlayNative.GetMonitorInfo(monitor, ref info)) return;
        var width = Math.Min(rect.Right - rect.Left, info.Work.Right - info.Work.Left);
        var height = Math.Min(rect.Bottom - rect.Top, info.Work.Bottom - info.Work.Top);
        var x = Math.Clamp(rect.Left, info.Work.Left, info.Work.Right - width);
        var y = Math.Clamp(rect.Top, info.Work.Top, info.Work.Bottom - height);
        if (x != rect.Left || y != rect.Top || width != rect.Right - rect.Left || height != rect.Bottom - rect.Top)
            OverlayNative.SetWindowPos(handle, 0, x, y, width, height, 0x4 | 0x10);
    }
    public void CloseForExit() { allowClose = true; Close(); }
}

internal static class OverlayNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
