using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Harmonica.Core;
using Harmonica.Windows;
using Microsoft.Win32;

namespace Harmonica.InputProbe;

/// <summary>Physical-input receiver: no PlaybackEngine, GameInputSession or SendInput path.</summary>
internal sealed class GuideProbeWindow : Window
{
    private readonly GuideEngine guide = new(ScoreCodec.Parse(new("只读接收测试", 120, 20, "1 1 0:2 （#3）:2 【2】 【【1】】")), GuideMode.WaitForKey);
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap, FontSize = 18, Margin = new(0, 12, 0, 12) };
    private readonly TextBox received = new() { AcceptsReturn = true, IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly List<object> evidence = [];
    private RawNoteInput? raw;
    private GuideTarget target;
    private HwndSource? source;
    private bool tracking;
    private int rawAttacks, ordinaryDown, ordinaryUp;
    public GuideProbeWindow()
    {
        Title = "跟谱 · 只读输入接收验证"; Width = 880; Height = 620; FontFamily = new("Microsoft YaHei UI");
        var layout = new DockPanel { Margin = new(24) }; var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "手动输入验证 · 不会模拟任何键鼠", FontSize = 24 });
        top.Children.Add(new TextBlock { Text = "请使用真实键盘和鼠标：Z → 松开再按 Z → 休止 → 左键＋中键＋C → 右键＋X → 右键＋逗号。\n倒计时内按住音键不计命中；鼠标修饰键可保持。F10 暂停/继续，F11 停止。\n窗口消息保持未处理，可检查正常输入是否仍传递。自动化合成按键不能替代物理 Raw Input 验收。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) });
        var controls = new WrapPanel();
        void Button(string text, Action action)
        {
            var b = new Button { Content = text, Padding = new(12, 7, 12, 7), Margin = new(0, 0, 8, 0) };
            b.Click += (_, _) => Guard(action); controls.Children.Add(b);
        }
        Button("开始 / 暂停 / 继续", Toggle); Button("停止并检查按住状态", Stop);
        Button("监听注册 / 释放自检", RegistrationCheck); Button("导出证据", Export);
        top.Children.Add(controls); top.Children.Add(state); DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top); layout.Children.Add(received); Content = layout;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle; target = GuideTarget.ForInputProbe(handle);
            source = HwndSource.FromHwnd(handle); source.AddHook(Receive); raw = new RawNoteInput(handle);
            raw.NotePressed += press =>
            {
                rawAttacks++; var accepted = guide.Accept(press);
                Record(new { kind = "RawAttack", press.VirtualKey, modifiers = press.Modifiers.ToString(), accepted, index = guide.Snapshot.EventIndex });
                Present();
            };
            raw.Faulted += ex => { guide.Pause(); state.Text = ex.Message; Record(new { kind = "Fault", error = ex.Message }); };
            Present();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F10) Guard(Toggle); if (e.Key == Key.F11) Guard(Stop); };
        timer.Tick += (_, _) => Guard(() =>
        {
            if (!target.IsForeground) { guide.Pause(); raw?.Stop(); timer.Stop(); Present(); return; }
            if (guide.Phase == GuidePhase.Countdown && guide.Snapshot.CountdownSeconds <= 0) raw!.Start(target);
            guide.Update(); if (guide.Phase == GuidePhase.Completed) { raw?.Stop(); timer.Stop(); }
            Present();
        });
        Closed += (_, _) => { timer.Stop(); raw?.Dispose(); source?.RemoveHook(Receive); };
    }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { guide.Pause(); timer.Stop(); state.Text = ex.Message; Record(new { kind = "Fault", error = ex.Message }); } }
    private void Toggle()
    {
        if (guide.IsActive) { guide.Pause(); raw!.Stop(); timer.Stop(); }
        else
        {
            tracking = true;
            if (guide.Phase == GuidePhase.Paused) guide.Resume(); else guide.Start();
            timer.Start();
        }
        Present();
    }
    private void Stop()
    {
        var before = Held(); guide.Stop(); raw?.Stop(); timer.Stop(); var after = Held();
        Record(new { kind = "Stop", before, after, unchanged = before.SequenceEqual(after) }); Present();
    }
    private static ushort[] Held() => PhysicalNoteTracker.NoteKeys.Concat(new ushort[] { 1, 2, 4 }).Where(k => (GetAsyncKeyState(k) & 0x8000) != 0).ToArray();
    private void RegistrationCheck()
    {
        Stop(); var baseline = RegistrationCount(); var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) { raw!.Start(target); raw.Stop(); }
        var final = RegistrationCount(); watch.Stop();
        if (final != baseline || raw!.Enabled) throw new Exception("Raw Input registration leaked");
        var result = new { kind = "RegistrationCheck", iterations = 100, baseline, final, milliseconds = watch.ElapsedMilliseconds, passed = true };
        Record(result); state.Text = "100 次注册/释放通过；尚未证明真实键鼠传递。";
    }
    private nint Receive(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        raw?.HandleMessage(message, wParam, lParam);
        if (!tracking) return 0;
        if (message is 0x100 or 0x101 && PhysicalNoteTracker.NoteKeys.Contains((ushort)wParam))
        {
            if (message == 0x100) ordinaryDown++; else ordinaryUp++;
            Record(new { kind = message == 0x100 ? "OrdinaryKeyDown" : "OrdinaryKeyUp", key = (ushort)wParam });
        }
        if (message is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208) Record(new { kind = "OrdinaryMouse", message });
        return 0; // Preserve legacy delivery and WM_INPUT cleanup. Never claim handled.
    }
    private void Present()
    {
        var p = guide.Snapshot;
        state.Text = $"{p.Phase} · 第 {Math.Min(p.EventIndex + 1, guide.Timeline.Count)} 音 · Raw 起音 {rawAttacks} · 普通按下/释放 {ordinaryDown}/{ordinaryUp}" +
            (p.Phase == GuidePhase.Countdown ? $"\n准备 {p.CountdownSeconds:0.0}s" : p.EventIndex < guide.Timeline.Count ? "\n" + GuideEngine.Describe(guide.Timeline.Score.Events[p.EventIndex]) : "");
    }
    private void Record(object value)
    {
        if (evidence.Count >= 10_000) evidence.RemoveAt(0);
        evidence.Add(value); received.AppendText(JsonSerializer.Serialize(value) + Environment.NewLine);
        if (received.LineCount > 300) received.Text = string.Join(Environment.NewLine, received.Text.Split(Environment.NewLine).TakeLast(200));
        received.ScrollToEnd();
    }
    private void Export()
    {
        var dialog = new SaveFileDialog { FileName = "guide-input-evidence.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { scope = "same-process physical receiver; not game validation", rawAttacks, ordinaryDown, ordinaryUp, events = evidence }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static uint RegistrationCount()
    {
        uint count = 0;
        if (GetRegisteredRawInputDevices(0, ref count, (uint)(8 + nint.Size)) == uint.MaxValue) throw new System.ComponentModel.Win32Exception();
        return count;
    }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRegisteredRawInputDevices(nint devices, ref uint count, uint size);
}
