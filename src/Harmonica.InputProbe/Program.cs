using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Harmonica.Core;
using Harmonica.Windows;
using Microsoft.Win32;

namespace Harmonica.InputProbe;

public static class Program
{
    [STAThread]
    public static void Main() => new Application().Run(new ProbeWindow());
}

/// <summary>Manual verification only. Nothing is sent until the user presses the test button.</summary>
internal sealed class ProbeWindow : Window
{
    private readonly Button start = new() { Content = "开始本窗口测试", Padding = new(16, 8, 16, 8), Margin = new(0, 0, 8, 0) };
    private readonly Button stop = new() { Content = "停止（F11）", Padding = new(16, 8, 16, 8), Margin = new(0, 0, 8, 0) };
    private readonly Button export = new() { Content = "导出接收证据", Padding = new(16, 8, 16, 8) };
    private readonly TextBlock status = new() { Text = "尚未测试；不会自动发送输入。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 12) };
    private readonly TextBox log = new()
    {
        IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas"), FontSize = 14,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(28, 34, 34)),
        Foreground = Brushes.WhiteSmoke, Padding = new(14), Focusable = true, IsReadOnlyCaretVisible = false
    };
    private PlaybackEngine? engine;
    private HotkeyManager? hotkeys;
    private readonly List<ReceivedInput> evidence = [];
    private volatile bool capturing;
    private bool closing;
    private bool allowClose;

    public ProbeWindow()
    {
        Title = "口琴小曲库 · 独立输入接收测试";
        Width = 900; Height = 680; MinWidth = 740; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(244, 247, 245));
        FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        var panel = new DockPanel { Margin = new(24) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "只验证本窗口收到的普通 Windows 输入", FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock
        {
            Text = "点击开始后有 3 秒倒计时。请将鼠标留在下方深色接收区，不要切换窗口或手动按键。\n" +
                   "测试谱：1 （1） 【1】 【2】 【【1】】 #1。预计 6 次键盘按下/释放、4 次鼠标按下/释放。\n" +
                   "本程序拒绝向其他进程窗口发送，切出窗口立即停止。F11 随时停止。测试不会启动游戏。",
            Margin = new(0, 12, 0, 16), TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(start); buttons.Children.Add(stop); buttons.Children.Add(export);
        heading.Children.Add(buttons); heading.Children.Add(status);
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading); panel.Children.Add(log);
        Content = panel;
        SourceInitialized += (_, _) => Initialize();
        start.Click += async (_, _) => await RunAsync();
        stop.Click += async (_, _) => { if (engine is not null) try { await engine.StopAsync(); } catch (Exception ex) { Append(ex.Message); } };
        export.Click += (_, _) => Export();
        Closing += async (_, args) =>
        {
            if (allowClose) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            try
            {
                if (engine is not null) await engine.DisposeAsync();
                hotkeys?.Dispose();
                allowClose = true;
                _ = Dispatcher.BeginInvoke(Close);
            }
            catch (Exception ex) { status.Text = "关闭前清理失败，请按停止重试：" + ex.Message; closing = false; }
        };
    }

    private void Initialize()
    {
        var handle = new WindowInteropHelper(this).Handle;
        engine = PlaybackEngine.CreateInputProbe(handle);
        engine.Diagnostic += text => Dispatcher.BeginInvoke(() => Append(text));
        engine.Progress += progress =>
        {
            if (progress.State == "Playing") capturing = true;
            Dispatcher.BeginInvoke(() => status.Text = $"{progress.State} · {Math.Max(0, progress.EventIndex + 1)}/{progress.EventCount}");
        };
        hotkeys = new HotkeyManager(handle);
        hotkeys.StopRequested += async () => { try { await engine.StopAsync(); } catch (Exception ex) { Append(ex.Message); } };
        if (!hotkeys.TryRegister("F10", "F11", out string error)) status.Text = error;
        HwndSource.FromHwnd(handle).AddHook(Receive);
    }

    private async Task RunAsync()
    {
        if (engine is null || engine.IsPlaying) return;
        start.IsEnabled = export.IsEnabled = false;
        evidence.Clear(); log.Clear(); capturing = false; log.Focus();
        string result = "Error";
        try
        {
            await engine.PlayAsync(ScoreCodec.Parse(new("原生输入接收测试", 120, 30, "1 （1） 【1】 【2】 【【1】】 #1")), PlaybackMode.Game);
            result = "Completed or stopped";
        }
        catch (Exception ex) { Append("失败：" + ex.Message); }
        finally
        {
            // Flush queued window messages, including the final key-up, before summarizing.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            capturing = false;
            var down = evidence.Count(item => item.Kind == "KeyDown");
            var up = evidence.Count(item => item.Kind == "KeyUp");
            var mouseDown = evidence.Count(item => item.Kind.EndsWith("Down") && !item.Kind.StartsWith("Key"));
            var mouseUp = evidence.Count(item => item.Kind.EndsWith("Up") && !item.Kind.StartsWith("Key"));
            bool matches = down == 6 && up == 6 && mouseDown == 4 && mouseUp == 4;
            status.Text = $"{(matches ? "接收计数匹配" : "接收计数未匹配或测试中止")} · 键盘 {down}↓ / {up}↑ · 鼠标 {mouseDown}↓ / {mouseUp}↑";
            Append($"Result: {result}; expected key 6/6, mouse 4/4; matches={matches}");
            start.IsEnabled = export.IsEnabled = true;
        }
    }

    private nint Receive(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        hotkeys?.HandleMessage(message, wParam, lParam);
        if (!capturing) return 0;
        var kind = message switch
        {
            0x100 or 0x104 => "KeyDown", 0x101 or 0x105 => "KeyUp",
            0x201 => "LeftDown", 0x202 => "LeftUp", 0x204 => "RightDown", 0x205 => "RightUp",
            0x207 => "MiddleDown", 0x208 => "MiddleUp", _ => null
        };
        if (kind is null) return 0;
        var item = new ReceivedInput(DateTimeOffset.UtcNow, kind, kind.StartsWith("Key") ? (int)wParam : 0);
        evidence.Add(item);
        Append($"{item.At:HH:mm:ss.fff} {item.Kind,-12} {(item.VirtualKey == 0 ? "" : $"VK 0x{item.VirtualKey:X2}")}");
        // Avoid text-selection, context menus, and shortcut actions while receiving test input.
        handled = true;
        return 0;
    }

    private void Append(string text) { log.AppendText(text + Environment.NewLine); log.ScrollToEnd(); }
    private void Export()
    {
        var dialog = new SaveFileDialog { Title = "保存本窗口输入接收证据", FileName = "input-probe-evidence.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new
        {
            scope = "Same-process foreground receiver only; not game validation", status = status.Text, events = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
        Append("证据已保存。");
    }
    private sealed record ReceivedInput(DateTimeOffset At, string Kind, int VirtualKey);
}
