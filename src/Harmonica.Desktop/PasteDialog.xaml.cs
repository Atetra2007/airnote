using System.Windows;
namespace Harmonica.Desktop;
public partial class PasteDialog : Window
{
    public string ScoreText => ScoreInput.Text;
    public PasteDialog() { InitializeComponent(); Loaded += (_, _) => ScoreInput.Focus(); }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ScoreText)) { MessageBox.Show(this, "请先粘贴曲谱。"); return; }
        DialogResult = true;
    }
}
