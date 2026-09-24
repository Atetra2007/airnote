using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Harmonica.Desktop.Services;

namespace Harmonica.Desktop;

public partial class App : Application
{
    private Mutex? singleton;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? waiter;
    private bool ownsMutex;
    public static Stopwatch StartupTimer { get; } = Stopwatch.StartNew();

    protected override async void OnStartup(StartupEventArgs e)
    {
        StartupTimer.Restart();
        base.OnStartup(e);
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HarmonicaDesk");
            var overrideIndex = Array.IndexOf(e.Args, "--data-dir");
            if (overrideIndex >= 0 && overrideIndex + 1 < e.Args.Length) root = Path.GetFullPath(e.Args[overrideIndex + 1]);
            // Explicit test profiles are independent; normal desktop launches keep the original mutex.
            var instanceSuffix = overrideIndex >= 0 && overrideIndex + 1 < e.Args.Length
                ? "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(root).ToUpperInvariant())))[..24]
                : "";
            singleton = new Mutex(true, @"Local\HarmonicaDesk.App" + instanceSuffix, out ownsMutex);
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HarmonicaDesk.Activate" + instanceSuffix);
            if (!ownsMutex) { activate.Set(); Shutdown(); return; }
            var store = new LibraryStore(root, Path.Combine(AppContext.BaseDirectory, "Resources"));
            await store.InitializeAsync();
            ThemeService.Apply(store.Settings.Theme);
            var window = new MainWindow(store);
            MainWindow = window;
            waiter = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow == null) return;
                if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
                MainWindow.Show(); MainWindow.Activate();
                SetForegroundWindow(new WindowInteropHelper(MainWindow).Handle);
            }), null, Timeout.Infinite, false);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开口琴小曲库，原有曲谱不会被删除。\n\n" + ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        waiter?.Unregister(null);
        activate?.Dispose();
        if (ownsMutex) singleton?.ReleaseMutex();
        singleton?.Dispose();
        base.OnExit(e);
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}
