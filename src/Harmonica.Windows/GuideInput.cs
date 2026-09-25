using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Harmonica.Windows;

public readonly record struct GuideTarget(nint Handle, uint ProcessId)
{
    public bool IsForeground => Handle != 0 && NativeMethods.GetForegroundWindow() == Handle &&
        NativeMethods.GetWindowThreadProcessId(Handle, out var id) != 0 && id == ProcessId;
    public bool Exists => Handle != 0 && GuideNative.IsWindow(Handle) &&
        NativeMethods.GetWindowThreadProcessId(Handle, out var id) != 0 && id == ProcessId;
    public static bool TryCaptureGame(out GuideTarget target, out string error)
    {
        target = default; error = "请切到三角洲行动的口琴界面后重试。";
        try
        {
            var window = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(window, out var id);
            if (window == 0 || id == 0) return false;
            using var process = Process.GetProcessById(checked((int)id));
            if (!string.Equals(process.ProcessName, "DeltaForceClient-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) return false;
            target = new(window, id); error = ""; return true;
        }
        catch (Exception ex) { error = "无法确认游戏前台窗口：" + ex.Message; return false; }
    }
    /// <summary>Development receiver only; cannot bind an arbitrary other process.</summary>
    public static GuideTarget ForInputProbe(nint handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out var id);
        if (handle == 0 || id != Environment.ProcessId) throw new ArgumentException("只允许绑定本进程的接收窗口。");
        return new(handle, id);
    }
    public (int Left, int Top, int Width, int Height)? ClientBounds()
    {
        if (!Exists || !GuideNative.GetClientRect(Handle, out var rect)) return null;
        var origin = new NativeMethods.POINT();
        if (!GuideNative.ClientToScreen(Handle, ref origin)) return null;
        return (origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
}

/// <summary>
/// One Raw Input registration per process. No hooks, key swallowing, injection, or unrelated-key logging.
/// HandleMessage must run on the window's dispatcher and leave WM_INPUT unhandled for DefWindowProc.
/// </summary>
public sealed class RawNoteInput : IDisposable
{
    private static RawNoteInput? owner;
    private readonly nint receiver;
    private readonly PhysicalNoteTracker tracker = new();
    private GuideTarget target;
    private bool registered, disposed;
    public bool Enabled { get; private set; }
    public event Action<GuideKeyPress>? NotePressed;
    public event Action<Exception>? Faulted;
    public RawNoteInput(nint receiver)
    {
        if (receiver == 0) throw new ArgumentException("输入监听需要有效窗口。", nameof(receiver));
        this.receiver = receiver;
    }
    public void Start(GuideTarget game)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!game.IsForeground) throw new InvalidOperationException("游戏当前不在前台，已暂停练习。");
        if (owner != null && owner != this) throw new InvalidOperationException("已有演奏输入监听正在运行。");
        target = game;
        if (!registered)
        {
            var devices = Devices(0x100 | 0x2000, receiver); // INPUTSINK | DEVNOTIFY; never NOLEGACY.
            if (!GuideNative.RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<GuideNative.RawDevice>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启动只读按键监听。可切换为节奏跟弹。");
            owner = this; registered = true;
        }
        SeedHeldKeys(); Enabled = true;
    }
    public void SeedHeldKeys()
    {
        bool Held(ushort key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        var modifiers = NoteModifiers.None;
        if (Held(1)) modifiers |= NoteModifiers.Left;
        if (Held(2)) modifiers |= NoteModifiers.Right;
        if (Held(4)) modifiers |= NoteModifiers.Middle;
        tracker.Reset(PhysicalNoteTracker.NoteKeys.Where(Held), modifiers);
    }
    public void Stop()
    {
        Enabled = false; tracker.Reset();
        if (!registered) return;
        // REMOVE requires a null hwndTarget, even though registration had a receiver.
        if (!GuideNative.RegisterRawInputDevices(Devices(1, 0), 2, (uint)Marshal.SizeOf<GuideNative.RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "停止输入监听失败；判定已停用。");
        registered = false;
        if (ReferenceEquals(owner, this)) owner = null;
    }
    private static GuideNative.RawDevice[] Devices(uint flags, nint handle) =>
        [new() { Page = 1, Usage = 6, Flags = flags, Window = handle }, new() { Page = 1, Usage = 2, Flags = flags, Window = handle }];

    public void HandleMessage(int message, nint wParam, nint lParam)
    {
        if (message is not (0x00FE or 0x00FF)) return;
        if (!Enabled || !target.IsForeground) return;
        if (message == 0x00FE) { SeedHeldKeys(); return; } // Device arrival/removal cannot leave a stale held key.
        if (message != 0x00FF) return;
        try
        {
            var size = (uint)Marshal.SizeOf<GuideNative.RawInput>();
            if (GuideNative.GetRawInputData(lParam, 0x10000003, out var input, ref size,
                (uint)Marshal.SizeOf<GuideNative.RawHeader>()) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "读取演奏按键失败。");
            if (Decode(input, tracker) is { } press) NotePressed?.Invoke(press);
        }
        catch (Exception ex) { Enabled = false; Faulted?.Invoke(ex); }
    }
    internal static GuideKeyPress? Decode(GuideNative.RawInput input, PhysicalNoteTracker tracker)
    {
        if (input.Header.Type == 0)
        {
            var flags = input.Data.Mouse.ButtonFlags;
            void Button(NoteModifiers button, int down, int up)
            {
                if ((flags & down) != 0) tracker.Mouse(button, true);
                if ((flags & up) != 0) tracker.Mouse(button, false);
            }
            Button(NoteModifiers.Left, 1, 2); Button(NoteModifiers.Right, 4, 8); Button(NoteModifiers.Middle, 16, 32);
        }
        else if (input.Header.Type == 1)
            return tracker.Key(input.Data.Keyboard.VKey, (input.Data.Keyboard.Flags & 1) == 0);
        return null;
    }
    public void Dispose()
    {
        if (disposed) return;
        Stop(); disposed = true;
    }
}

internal static class GuideNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawDevice { public ushort Page, Usage; public uint Flags; public nint Window; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawHeader { public uint Type, Size; public nint Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawKeyboard
    { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct RawMouse
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct RawData
    { [FieldOffset(0)] public RawMouse Mouse; [FieldOffset(0)] public RawKeyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawInput { public RawHeader Header; public RawData Data; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputData(nint input, uint command, out RawInput data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(nint window, ref NativeMethods.POINT point);
}
