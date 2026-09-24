using System.Diagnostics;
using System.Windows;
using Harmonica.Desktop.Models;

namespace Harmonica.Desktop;

public partial class SettingsDialog : Window
{
    private readonly string root;
    public string ThemeChoice => ThemeDark.IsChecked == true ? "Dark" : ThemeLight.IsChecked == true ? "Light" : "System";
    public string StartHotkey => StartInput.Text.Trim();
    public string StopHotkey => StopInput.Text.Trim();
    public string OverlayHotkey => OverlayInput.Text.Trim();
    public int GapMs { get; private set; }
    public int GameNoteGapMs { get; private set; }
    public SettingsDialog(UserSettings settings, int gap, string root)
    {
        this.root = root; InitializeComponent();
        ThemeSystem.IsChecked = settings.Theme == "System";
        ThemeLight.IsChecked = settings.Theme == "Light";
        ThemeDark.IsChecked = settings.Theme == "Dark";
        StartInput.Text = settings.StartHotkey; StopInput.Text = settings.StopHotkey; GapInput.Text = gap.ToString();
        OverlayInput.Text = settings.OverlayHotkey;
        GameGapInput.Text = settings.GameNoteGapMs.ToString();
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        void Error(string text) { ErrorText.Text = text; ErrorText.Visibility = Visibility.Visible; }
        if (!int.TryParse(GapInput.Text, out int gap) || gap is < 10 or > 5000) { Error("音符间隔须为 10–5000 的整数。"); return; }
        if (!int.TryParse(GameGapInput.Text, out int gameGap) || gameGap is < 0 or > 250) { Error("游戏换音留白须为 0–250 的整数；0 表示只用曲谱间隔。"); return; }
        if (!Harmonica.Windows.HotkeyChord.TryParse(StartHotkey, out var start, out var error) ||
            !Harmonica.Windows.HotkeyChord.TryParse(StopHotkey, out var stop, out error) ||
            !Harmonica.Windows.HotkeyChord.TryParse(OverlayHotkey, out var overlay, out error)) { Error(error); return; }
        if (start!.VirtualKey == stop!.VirtualKey) { Error("开始和停止必须使用不同的 F 键。"); return; }
        if (overlay!.VirtualKey == start.VirtualKey || overlay.VirtualKey == stop.VirtualKey) { Error("浮窗热键与开始 / 停止冲突，请选择另一个 F 键。原设置尚未更改。"); return; }
        GapMs = gap; GameNoteGapMs = gameGap; DialogResult = true;
    }
    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(root) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开目录"); }
    }
}
