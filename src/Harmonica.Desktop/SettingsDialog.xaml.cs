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
    public int GapMs { get; private set; }
    public SettingsDialog(UserSettings settings, int gap, string root)
    {
        this.root = root; InitializeComponent();
        ThemeSystem.IsChecked = settings.Theme == "System";
        ThemeLight.IsChecked = settings.Theme == "Light";
        ThemeDark.IsChecked = settings.Theme == "Dark";
        StartInput.Text = settings.StartHotkey; StopInput.Text = settings.StopHotkey; GapInput.Text = gap.ToString();
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(GapInput.Text, out int gap) || gap is < 10 or > 5000) { MessageBox.Show(this, "音符间隔须为 10–5000 的整数。"); return; }
        if (string.IsNullOrWhiteSpace(StartHotkey) || string.IsNullOrWhiteSpace(StopHotkey)) { MessageBox.Show(this, "请填写开始与停止快捷键。"); return; }
        GapMs = gap; DialogResult = true;
    }
    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(root) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开目录"); }
    }
}
