using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Harmonica.Windows;

public sealed record HotkeyChord(uint Modifiers, uint VirtualKey)
{
    public static bool TryParse(string? text, out HotkeyChord? chord, out string error)
    {
        chord = null;
        error = "热键格式示例：F6、Ctrl+F6、Alt+Shift+F8；只支持 F1–F12。";
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            uint modifier = parts[i].ToUpperInvariant() switch { "ALT" => 1, "CTRL" => 2, "SHIFT" => 4, _ => 0 };
            if (modifier == 0 || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        var key = parts[^1];
        if (key.Length is < 2 or > 3 || char.ToUpperInvariant(key[0]) != 'F' || key[1] == '0' ||
            key.AsSpan(1).ContainsAnyExceptInRange('0', '9') ||
            !int.TryParse(key.AsSpan(1), out int number) || number is < 1 or > 12) return false;
        chord = new(modifiers, (uint)(0x70 + number - 1));
        error = "";
        return true;
    }
}

public sealed class HotkeyManager : IDisposable
{
    private readonly nint window;
    private const int StartId = 0x4B31;
    private const int StopId = 0x4B32;
    private bool disposed;
    public bool StartRegistered { get; private set; }
    public bool StopRegistered { get; private set; }
    public event Action? StartRequested;
    public event Action? StopRequested;

    public HotkeyManager(nint window)
    {
        if (window == 0) throw new ArgumentException("热键需要一个有效窗口。", nameof(window));
        this.window = window;
    }

    public bool TryRegister(string start, string stop, out string error)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!HotkeyChord.TryParse(start, out var startChord, out error) ||
            !HotkeyChord.TryParse(stop, out var stopChord, out error)) return false;
        if (startChord!.VirtualKey == stopChord!.VirtualKey)
        {
            error = "开始和停止必须使用不同的 F 键，避免停止与开始冲突。";
            return false;
        }
        Unregister();
        // Reserve the emergency stop first. A partial registration is never kept.
        StopRegistered = NativeMethods.RegisterHotKey(window, StopId, stopChord.Modifiers | 0x4000, stopChord.VirtualKey);
        if (!StopRegistered)
        {
            error = $"停止热键 {stop} 注册失败：{new Win32Exception(Marshal.GetLastWin32Error()).Message}。游戏演奏已禁用。";
            return false;
        }
        StartRegistered = NativeMethods.RegisterHotKey(window, StartId, startChord.Modifiers | 0x4000, startChord.VirtualKey);
        if (!StartRegistered)
        {
            var code = Marshal.GetLastWin32Error();
            Unregister();
            error = $"开始热键 {start} 注册失败：{new Win32Exception(code).Message}。游戏演奏已禁用。";
            return false;
        }
        error = "";
        return true;
    }

    public bool HandleMessage(int message, nint wParam, nint lParam)
    {
        if (disposed || message != 0x0312) return false;
        if (wParam == StartId && StartRegistered) { StartRequested?.Invoke(); return true; }
        if (wParam == StopId && StopRegistered) { StopRequested?.Invoke(); return true; }
        return false;
    }

    private void Unregister()
    {
        if (StartRegistered) NativeMethods.UnregisterHotKey(window, StartId);
        if (StopRegistered) NativeMethods.UnregisterHotKey(window, StopId);
        StartRegistered = StopRegistered = false;
    }

    public void Dispose() { if (disposed) return; Unregister(); disposed = true; }
}
