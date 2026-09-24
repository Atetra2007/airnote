using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Harmonica.Core;

namespace Harmonica.Windows;

public readonly record struct PerformanceKey(ushort VirtualKey, bool Mouse = false)
{
    public override string ToString() => Mouse ? VirtualKey switch
    { 1 => "鼠标左键", 2 => "鼠标右键", 4 => "鼠标中键", _ => $"Mouse {VirtualKey}" }
        : VirtualKey == 0xBC ? "," : ((char)VirtualKey).ToString();
}

public static class GameKeyMapping
{
    public static IReadOnlyList<PerformanceKey> ForNote(ScoreEvent note)
    {
        if (note.Degree == 0) return [];
        if (note.Degree is < 1 or > 7 || note.Octave is < -1 or > 2 ||
            note.Octave == 2 && note.Degree != 1)
            throw new InvalidOperationException($"游戏不支持音符 {note.Label} 的音域。");
        List<PerformanceKey> keys = [];
        if (note.Octave == -1) keys.Add(new(1, true));
        if (note.Octave == 1 && note.Degree != 1 || note.Octave == 2) keys.Add(new(2, true));
        if (note.Sharp) keys.Add(new(4, true));
        keys.Add(new((ushort)(note.Octave >= 1 && note.Degree == 1 ? 0xBC : "ZXCVBNM"[note.Degree - 1])));
        return keys;
    }

    public static double Frequency(ScoreEvent note)
    {
        if (note.Degree == 0) return 0;
        if (note.Degree is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(note));
        int[] semitones = [0, 2, 4, 5, 7, 9, 11];
        var midi = 60 + note.Octave * 12 + semitones[note.Degree - 1] + (note.Sharp ? 1 : 0);
        return 440d * Math.Pow(2, (midi - 69) / 12d);
    }
}

internal interface IInputBackend
{
    nint ForegroundWindow { get; }
    nint CursorRootWindow { get; }
    uint WindowProcess(nint window);
    string ProcessName(uint process);
    bool IsDown(ushort virtualKey);
    void Send(PerformanceKey key, bool down);
}

internal sealed class WindowsInputBackend : IInputBackend
{
    public nint ForegroundWindow => NativeMethods.GetForegroundWindow();
    public nint CursorRootWindow
    {
        get
        {
            if (!NativeMethods.GetCursorPos(out var point)) return 0;
            return NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), 2);
        }
    }
    public uint WindowProcess(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId;
    }
    public string ProcessName(uint process)
    {
        using var item = Process.GetProcessById(checked((int)process));
        return item.ProcessName;
    }
    public bool IsDown(ushort key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    public void Send(PerformanceKey key, bool down)
    {
        var input = new NativeMethods.INPUT();
        if (key.Mouse)
        {
            input.type = 0;
            input.data.mi.dwFlags = (key.VirtualKey, down) switch
            {
                (1, true) => 0x0002, (1, false) => 0x0004,
                (2, true) => 0x0008, (2, false) => 0x0010,
                (4, true) => 0x0020, (4, false) => 0x0040,
                _ => throw new ArgumentOutOfRangeException(nameof(key))
            };
        }
        else
        {
            input.type = 1;
            input.data.ki.wVk = key.VirtualKey;
            input.data.ki.dwFlags = down ? 0u : 0x0002u;
        }
        if (NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>()) != 1)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Windows 拒绝了 {key} {(down ? "按下" : "释放")} 输入" +
                $"（错误 {error}）。输入可能被权限限制或游戏拦截；程序不会自动提权。" +
                (error == 0 ? "" : new Win32Exception(error).Message));
        }
    }
}

internal sealed class GameInputSession
{
    private readonly IInputBackend backend;
    private readonly nint probeWindow;
    private readonly IPlaybackClock clock;
    private readonly List<PerformanceKey> attempted = [];
    private nint window;
    private uint processId;
    private static readonly ushort[] ConflictKeys = [
        0x10, 0x11, 0x12, 0x5B, 0x5C, // Shift, Control, Alt, Windows
        1, 2, 4, (ushort)'Z', (ushort)'X', (ushort)'C', (ushort)'V',
        (ushort)'B', (ushort)'N', (ushort)'M', 0xBC];

    public const double ModifierSettleSeconds = .024;

    public GameInputSession(IInputBackend backend, nint probeWindow = default, IPlaybackClock? clock = null)
    {
        this.backend = backend;
        this.probeWindow = probeWindow;
        this.clock = clock ?? new MonotonicPlaybackClock();
    }

    public void Begin()
    {
        window = backend.ForegroundWindow;
        processId = backend.WindowProcess(window);
        if (window == 0 || processId == 0) throw new InvalidOperationException("无法确认前台窗口，已停止演奏。");
        if (probeWindow != 0)
        {
            if (window != probeWindow || processId != Environment.ProcessId)
                throw new InvalidOperationException("测试只能向当前进程自己的前台接收窗口发送输入。");
        }
        else if (!string.Equals(backend.ProcessName(processId), "DeltaForceClient-Win64-Shipping", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("倒计时结束时前台不是三角洲行动客户端，已停止。请切回游戏后重试。");
        CheckSafety();
    }

    public bool StartupKeysHeld => Enumerable.Range(0x70, 12).Any(k => backend.IsDown((ushort)k)) ||
        new ushort[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(backend.IsDown);

    public void CheckSafety()
    {
        if (window == 0 || backend.ForegroundWindow != window || backend.WindowProcess(window) != processId)
            throw new InvalidOperationException("前台窗口已经变化，演奏已停止。");
        if (attempted.Any(key => key.Mouse) && backend.CursorRootWindow != window)
            throw new InvalidOperationException("鼠标已移出目标窗口，演奏已停止。");
        foreach (var key in ConflictKeys)
            if (backend.IsDown(key) && !attempted.Any(owned => owned.VirtualKey == key))
                throw new InvalidOperationException("检测到手动修饰键、鼠标或演奏按键冲突，演奏已停止。请松开按键后重试。");
    }

    public async Task NoteOnAsync(ScoreEvent note, CancellationToken token = default)
    {
        if (attempted.Count != 0) throw new InvalidOperationException("上一个音符尚未释放，已停止。");
        CheckSafety();
        var keys = GameKeyMapping.ForNote(note);
        foreach (var key in keys)
        {
            token.ThrowIfCancellationRequested();
            if (!key.Mouse && keys.Any(k => k.Mouse))
            {
                // The game samples pitch modifiers separately from the note key.
                var readyAt = clock.Seconds + ModifierSettleSeconds;
                while (clock.Seconds < readyAt)
                {
                    token.ThrowIfCancellationRequested();
                    CheckSafety();
                    await clock.DelayAsync(TimeSpan.FromSeconds(Math.Min(.008, readyAt - clock.Seconds)), token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
            }
            CheckSafety();
            if (key.Mouse && backend.CursorRootWindow != window)
                throw new InvalidOperationException("鼠标不在目标窗口内，已停止鼠标输入。");
            // Keep attempts even when SendInput fails: partial/uncertain delivery needs a matching key-up.
            attempted.Add(key);
            backend.Send(key, true);
        }
    }

    public async Task ReleaseAsync()
    {
        List<Exception> lastErrors = [];
        for (var attempt = 0; attempt < 3 && attempted.Count > 0; attempt++)
        {
            lastErrors.Clear();
            for (var i = attempted.Count - 1; i >= 0; i--)
            {
                try { backend.Send(attempted[i], false); attempted.RemoveAt(i); }
                catch (Exception ex) { lastErrors.Add(ex); }
            }
            if (attempted.Count > 0 && attempt < 2) await Task.Delay(15).ConfigureAwait(false);
        }
        if (attempted.Count > 0)
            throw new AggregateException("无法确认以下按键已释放：" + string.Join("、", attempted) +
                "。请按停止重试，必要时手动按下并松开这些键。", lastErrors);
    }
}

internal static partial class NativeMethods
{
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);

    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT
    { public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT
    { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }
}
