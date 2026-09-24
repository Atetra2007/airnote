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
    public double? DurationSeconds { get; set; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtraData { get; set; }
    [JsonIgnore] public bool Available { get; set; }
    [JsonIgnore] public string[] Aliases { get; set; } = [];
    [JsonIgnore] public string Variant { get; set; } = "";
    [JsonIgnore] public string SourceAuthor { get; set; } = "";
    [JsonIgnore] public string FavoriteGlyph => Favorite ? "★" : "☆";
    [JsonIgnore] public string Subtitle => $"{(Example ? "原创示例" : Available ? "本地曲谱" : "点击下载")}  ·  {Bpm} BPM";
    [JsonIgnore] public string EditionLabel => string.Join(" · ", new[] { Variant, DurationSeconds is > 0 ? FormatDuration(DurationSeconds.Value) : "" }.Where(s => s.Length > 0));
    [JsonIgnore] public string AvailabilityGlyph => Available ? "" : "↓";
    public bool Matches(string query) => string.Join(" ", new[] { Title, Bpm.ToString(), SourceAuthor }.Concat(Aliases)).Contains(query, StringComparison.OrdinalIgnoreCase);
    public static string FormatDuration(double seconds) { var whole = (long)Math.Round(seconds); return $"{whole / 60:00}:{whole % 60:00}"; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public sealed record CatalogSong(string Id, string Title, string FileName, int Bpm, long Size, string Sha256, string DownloadUrl, string SourceUrl)
{
    public string[] Aliases { get; init; } = [];
    public string Variant { get; init; } = "来源版本";
    public string Version { get; init; } = "";
    public double? DurationSeconds { get; init; }
    public string Author { get; init; } = "来源未注明";
    public string RhythmStatus { get; init; } = "not-reviewed";
    public string ReviewNote { get; init; } = "音乐节奏尚未复核。";
    public string? EvidenceUrl { get; init; }
    public string DownloadFormat { get; init; } = "harmonica-txt";
    public string? ConvertedSha256 { get; init; }
    public string Description => $"{Variant} · {Author}\n{ReviewNote}";
}

public sealed class UserSettings
{
    public string Theme { get; set; } = "System";
    public string StartHotkey { get; set; } = "F6";
    public string StopHotkey { get; set; } = "F8";
    public string OverlayHotkey { get; set; } = "F7";
    public GuidePreferences Guide { get; set; } = new();
    public float Volume { get; set; } = 0.2f;
    public string? LastSelectedId { get; set; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtraData { get; set; }
}

public sealed class GuidePreferences
{
    public string Mode { get; set; } = "Rhythm";
    public double Speed { get; set; } = 1;
    public double Opacity { get; set; } = .75;
    public double Width { get; set; } = 560;
    public double Height { get; set; } = 280;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public void Normalize()
    {
        if (Mode is not ("Rhythm" or "WaitForKey")) Mode = "Rhythm";
        if (Speed is not (.5 or .75 or 1 or 1.25 or 1.5)) Speed = 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .35, .95) : .75;
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 440, 1600) : 560;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 240, 1000) : 280;
        if (Left is { } x && !double.IsFinite(x)) Left = null;
        if (Top is { } y && !double.IsFinite(y)) Top = null;
    }
}
