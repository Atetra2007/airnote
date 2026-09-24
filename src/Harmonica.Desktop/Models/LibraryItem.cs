using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Harmonica.Desktop.Models;

public sealed class LibraryItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名曲谱";
    public string? FileName { get; set; }
    public string? SourceId { get; set; }
    public string? Hash { get; set; }
    public int Bpm { get; set; } = 120;
    public bool Example { get; set; }
    public bool Favorite { get; set; }
    public bool NeedsBpmConfirmation { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
    [JsonIgnore] public bool Available { get; set; }
    [JsonIgnore] public string FavoriteGlyph => Favorite ? "★" : "☆";
    [JsonIgnore] public string Subtitle => $"{(Example ? "原创示例" : Available ? "本地曲谱" : "点击下载")}  ·  {Bpm} BPM";
    [JsonIgnore] public string AvailabilityGlyph => Available ? "" : "↓";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public sealed record CatalogSong(string Id, string Title, string FileName, int Bpm, long Size, string Sha256, string DownloadUrl, string SourceUrl);

public sealed class UserSettings
{
    public string Theme { get; set; } = "System";
    public string StartHotkey { get; set; } = "F6";
    public string StopHotkey { get; set; } = "F8";
    public float Volume { get; set; } = 0.2f;
    public string? LastSelectedId { get; set; }
}
