using System.Runtime.InteropServices;
using System.Windows.Interop;
using Harmonica.Desktop;
using Harmonica.Desktop.Models;

internal static class NativeOverlayChecks
{
    public static int Run()
    {
        // Construct only our own hidden HWNDs. No UI automation, game, focus change or input injection.
        var app = Program.CreateOffscreenApplication();
        for (var i = 0; i < 30; i++)
        {
            var window = new GuideOverlayWindow(new GuidePreferences { Left = -100_000, Top = -100_000 });
            var handle = new WindowInteropHelper(window).EnsureHandle();
            CheckStyle(window, editable: false);
            if (!GetWindowRect(handle, out var rect)) throw new Exception("Window bounds unavailable");
            var monitor = MonitorFromWindow(handle, 2); var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) throw new Exception("Monitor bounds unavailable");
            if (rect.Left < info.Work.Left || rect.Top < info.Work.Top || rect.Right > info.Work.Right || rect.Bottom > info.Work.Bottom)
                throw new Exception("Lost monitor position did not recover into current work area");
            window.SetEditable(true); CheckStyle(window, editable: true);
            window.SetEditable(false); CheckStyle(window, editable: false);
            if (window.IsVisible) throw new Exception("Native handle checks must not show a desktop window");
            handle = new WindowInteropHelper(window).Handle;
            window.CloseForExit();
            if (IsWindow(handle)) throw new Exception("Overlay HWND survived CloseForExit");
        }
        Console.WriteLine("PASS: 30 hidden overlay HWNDs: layered/noactivate/transparent/toolwindow flags, edit-lock round trips, out-of-screen recovery and native handle disposal. No live interaction or focus test.");
        return 0;
    }
    private static void CheckStyle(GuideOverlayWindow window, bool editable)
    {
        if (window.InputStyleError != null) throw new Exception(window.InputStyleError);
        var style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();
        const long mask = 0x08000000 | 0x20 | 0x80;
        if ((style & mask) != (editable ? 0 : mask)) throw new Exception($"Unexpected extended style: 0x{style:X}");
        if ((style & 0x80000) == 0) throw new Exception("Overlay must remain a layered window");
        if (window.ShowInTaskbar != editable || window.Owner != null || window.ShowActivated) throw new Exception("Overlay ownership/activation configuration changed");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
}
