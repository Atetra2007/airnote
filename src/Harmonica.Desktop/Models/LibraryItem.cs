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
    public string WorkId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public bool IsDefaultVariant { get; set; }
    public string Acquisition { get; set; } = "external-import";
    public string? VariantLabelOverride { get; set; }
    public string? Hash { get; set; }
    public string? VerifiedSha256 { get; set; }
    public string RhythmStatus { get; set; } = "not-reviewed";
    public string ReviewNote { get; set; } = "音乐节奏尚未复核。";
    public ReferenceMetadata? Reference { get; set; }
    public bool CalibrationRevoked { get; set; }
    public int Bpm { get; set; } = 120;
    public bool BpmKnown { get; set; } = true;
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
    [JsonIgnore] public bool IsCalibrated => Available && !CalibrationRevoked && IsSha256(Hash) && IsSha256(VerifiedSha256) &&
        string.Equals(Hash, VerifiedSha256, StringComparison.OrdinalIgnoreCase) && string.Equals(RhythmStatus, "reviewed", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public string RhythmLabel => Example ? "原创示例" : IsCalibrated ? "已复核片段"
        : string.Equals(RhythmStatus, "reviewed", StringComparison.OrdinalIgnoreCase) ? (Available ? "校准未匹配" : "已复核片段 · 待导入")
        : string.Equals(RhythmStatus, "deferred", StringComparison.OrdinalIgnoreCase) ? "暂缓校对" : "参考谱 · 未校准";
    [JsonIgnore] public string VariantDisplay => string.Join(" · ", new[] { string.IsNullOrWhiteSpace(Variant) ? "默认版本" : Variant, BpmKnown ? Bpm + " BPM" : "速度待确认" });
    [JsonIgnore] public string FavoriteGlyph => Favorite ? "★" : "☆";
    [JsonIgnore] public string Subtitle => $"{(Example ? "原创示例" : Available ? "本地曲谱" : Acquisition == "external-import" ? "需导入" : "可下载")}  ·  {(BpmKnown ? Bpm + " BPM" : "速度待确认")}";
    [JsonIgnore] public string EditionLabel => string.Join(" · ", new[] { Variant, DurationSeconds is > 0 ? FormatDuration(DurationSeconds.Value) : "" }.Where(s => s.Length > 0));
    [JsonIgnore] public string AvailabilityGlyph => Available ? "" : "↓";
    public bool Matches(string query) => string.Join(" ", new[] { Title, BpmKnown ? Bpm.ToString() : "速度待确认", SourceAuthor }.Concat(Aliases)).Contains(query, StringComparison.OrdinalIgnoreCase);
    private static bool IsSha256(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    public static string FormatDuration(double seconds) { var whole = (long)Math.Round(seconds); return $"{whole / 60:00}:{whole % 60:00}"; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public sealed class LibraryWork
{
    public string WorkId { get; }
    public IReadOnlyList<LibraryItem> Variants { get; }
    public LibraryItem SelectedVariant { get; }
    public string Title => SelectedVariant.Title;
    public bool Favorite => Variants.Any(item => item.Favorite);
    public string FavoriteGlyph => Favorite ? "★" : "☆";
    public string Subtitle => Variants.Count > 1
        ? $"{Variants.Count} 个版本 · {(Variants.Any(item => item.Available) ? "本地已有" : SelectedVariant.Acquisition == "external-import" ? "需导入" : "可下载")} · {SelectedVariant.RhythmLabel}"
        : $"{SelectedVariant.Subtitle} · {SelectedVariant.RhythmLabel}";
    public string EditionLabel => Variants.Count > 1 ? SelectedVariant.VariantDisplay : SelectedVariant.EditionLabel;
    public DateTimeOffset? LastPlayed => Variants.Where(item => item.LastPlayed != null).Select(item => item.LastPlayed).Max();

    public LibraryWork(string workId, IEnumerable<LibraryItem> variants, string? preferredItemId = null)
    {
        WorkId = workId;
        var ordered = variants.OrderByDescending(item => item.IsDefaultVariant).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("作品至少要有一个版本。", nameof(variants));
        Variants = ordered;
        SelectedVariant = ordered.FirstOrDefault(item => item.Id == preferredItemId)
            ?? ordered.FirstOrDefault(item => item.IsDefaultVariant)
            ?? ordered[0];
    }

    public bool Matches(string query) => Variants.Any(item => item.Matches(query));
}

public sealed record CatalogSong(string Id, string Title, string FileName, int Bpm, long Size, string? Sha256, string? DownloadUrl, string? SourceUrl)
{
    public bool BpmKnown { get; init; } = true;
    public string[] Aliases { get; init; } = [];
    public string WorkId { get; init; } = "";
    public string VariantId { get; init; } = "";
    public bool IsDefaultVariant { get; init; }
    public string Acquisition { get; init; } = "direct-download";
    public string Variant { get; init; } = "来源版本";
    public string Version { get; init; } = "";
    public double? DurationSeconds { get; init; }
    public string Author { get; init; } = "来源未注明";
    public string RhythmStatus { get; init; } = "not-reviewed";
    public string ReviewNote { get; init; } = "音乐节奏尚未复核。";
    public string? EvidenceUrl { get; init; }
    public string? VerifiedSha256 { get; init; }
    public ReferenceMetadata? Reference { get; init; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtraData { get; init; }
    public string DownloadFormat { get; init; } = "harmonica-txt";
    public string? ConvertedSha256 { get; init; }
    public string Description => $"{Variant} · {Author}\n{ReviewNote}";
}

public sealed class ReferenceMetadata
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    [JsonPropertyName("artist")]
    public string? Artist { get; set; }
    [JsonPropertyName("edition")]
    public string? Edition { get; set; }
    [JsonPropertyName("audio_sha256")]
    public string? AudioSha256 { get; set; }
    [JsonPropertyName("score_sha256")]
    public string? ScoreSha256 { get; set; }
    [JsonPropertyName("start_seconds")]
    public double? StartSeconds { get; set; }
    [JsonPropertyName("segment_start_seconds")]
    public double? SegmentStartSeconds { get; set; }
    [JsonPropertyName("segment_end_seconds")]
    public double? SegmentEndSeconds { get; set; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtraData { get; set; }
}

public sealed class UserSettings
{
    public string Theme { get; set; } = "System";
    public string StartHotkey { get; set; } = "F6";
    public string StopHotkey { get; set; } = "F8";
    public string OverlayHotkey { get; set; } = "F7";
    public GuidePreferences Guide { get; set; } = new();
    public float Volume { get; set; } = 0.2f;
    public int GameNoteGapMs { get; set; } = 60;
    public string? LastSelectedId { get; set; }
    public Dictionary<string, string> SelectedVariants { get; set; } = new(StringComparer.Ordinal);
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
