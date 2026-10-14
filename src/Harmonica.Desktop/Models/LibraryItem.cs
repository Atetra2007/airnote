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
    [JsonIgnore] public string ReferenceLabel => Reference?.Type switch
    {
        "published-sheet" => "参考乐谱：",
        "recording" => "参考录音：",
        _ => "参考："
    };
    [JsonIgnore] public string RhythmLabel => Example ? "原创示例" : IsCalibrated ? "已复核片段"
        : string.Equals(RhythmStatus, "reviewed", StringComparison.OrdinalIgnoreCase) ? (Available ? "校准未匹配" : "已复核片段 · 待导入")
        : string.Equals(RhythmStatus, "deferred", StringComparison.OrdinalIgnoreCase) ? "暂缓校对" : "参考谱 · 未校准";
    [JsonIgnore] public string VariantDisplay => string.Join(" · ", new[] { string.IsNullOrWhiteSpace(Variant) ? "默认版本" : Variant, BpmKnown ? Bpm + " BPM" : "速度待确认" });
    [JsonIgnore] public string FavoriteGlyph => Favorite ? "★" : "☆";
    [JsonIgnore] public string Subtitle => $"{(Example ? "原创示例" : Available ? "本地曲谱" : Acquisition == "external-import" ? "需导入" : "可下载")}  ·  {(BpmKnown ? Bpm + " BPM" : "速度待确认")}";
    [JsonIgnore] public string EditionLabel => string.Join(" · ", new[] { Variant, DurationSeconds is > 0 ? FormatDuration(DurationSeconds.Value) : "" }.Where(s => s.Length > 0));
    [JsonIgnore] public string AvailabilityGlyph => Available ? "" : "↓";
    public bool Matches(string query) => new LibrarySearchQuery(query).Match(this) != null;
    public bool MatchesQualityFilter(LibraryQualityFilter filter) => filter switch
    {
        LibraryQualityFilter.LocalAvailable => Available,
        LibraryQualityFilter.ReviewedFragment => IsCalibrated || (!Available && !CalibrationRevoked &&
            string.Equals(RhythmStatus, "reviewed", StringComparison.OrdinalIgnoreCase) && IsSha256(VerifiedSha256)),
        LibraryQualityFilter.UnknownBpm => !BpmKnown,
        _ => true
    };
    private static bool IsSha256(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    public static string FormatDuration(double seconds) { var whole = (long)Math.Round(seconds); return $"{whole / 60:00}:{whole % 60:00}"; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public enum LibraryQualityFilter { All, LocalAvailable, ReviewedFragment, UnknownBpm }

public sealed class LibraryWork
{
    public string WorkId { get; }
    public IReadOnlyList<LibraryItem> Variants { get; }
    public IReadOnlyList<LibraryItem> AllVariants { get; }
    public bool IsFiltered { get; }
    public string PreferredVariantId { get; }
    public bool HasTemporarySelection => SelectedVariant.Id != PreferredVariantId;
    public LibraryItem SelectedVariant { get; }
    public string Title => SelectedVariant.Title;
    public bool Favorite => AllVariants.Any(item => item.Favorite);
    public string FavoriteGlyph => Favorite ? "★" : "☆";
    public string Subtitle => Variants.Count > 1 || IsFiltered
        ? $"{Variants.Count} 个{(IsFiltered ? "匹配" : "")}版本 · {(Variants.Any(item => item.Available) ? "本地已有" : SelectedVariant.Acquisition == "external-import" ? "需导入" : "可下载")} · {SelectedVariant.RhythmLabel}"
        : $"{SelectedVariant.Subtitle} · {SelectedVariant.RhythmLabel}";
    public string EditionLabel => Variants.Count > 1 ? SelectedVariant.VariantDisplay : SelectedVariant.EditionLabel;
    public DateTimeOffset? LastPlayed => AllVariants.Where(item => item.LastPlayed != null).Select(item => item.LastPlayed).Max();
    public int SearchScore { get; }
    public string MatchHint { get; }
    public bool HasMatchHint => MatchHint.Length > 0;
    public string VersionBadge => IsFiltered ? $"{Variants.Count}/{AllVariants.Count} 版" : $"{Variants.Count} 版";
    public string ListDetail => $"{(SelectedVariant.Available ? "本地" : SelectedVariant.Acquisition == "direct-download" ? "可下载" : "待导入")} · {(SelectedVariant.BpmKnown ? SelectedVariant.Bpm + " BPM" : "速度待确认")} · {SelectedVariant.RhythmLabel}";
    public string ListTooltip => string.Join("\n", new[] { Title, ListDetail, SelectedVariant.VariantDisplay, MatchHint }.Where(value => value.Length > 0));

    public LibraryWork(string workId, IEnumerable<LibraryItem> variants, string? preferredItemId = null,
        IEnumerable<LibraryItem>? allVariants = null, bool isFiltered = false, string? savedPreferredItemId = null,
        int searchScore = 0, string matchHint = "")
    {
        WorkId = workId;
        var ordered = variants.OrderByDescending(item => item.IsDefaultVariant).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("作品至少要有一个版本。", nameof(variants));
        Variants = ordered;
        AllVariants = (allVariants ?? ordered).ToArray();
        var savedPreference = savedPreferredItemId ?? preferredItemId;
        PreferredVariantId = AllVariants.FirstOrDefault(item => item.Id == savedPreference)?.Id
            ?? BestAvailable(AllVariants).Id;
        IsFiltered = isFiltered || ordered.Length < AllVariants.Count;
        SearchScore = searchScore;
        MatchHint = matchHint;
        SelectedVariant = ordered.FirstOrDefault(item => item.Id == preferredItemId)
            ?? ordered.FirstOrDefault(item => item.Id == PreferredVariantId)
            ?? BestAvailable(ordered);
    }

    private static LibraryItem BestAvailable(IEnumerable<LibraryItem> items) => items
        .OrderByDescending(item => item.IsCalibrated).ThenByDescending(item => item.Available)
        .ThenByDescending(item => item.IsDefaultVariant).First();

    public bool Matches(string query) => Variants.Any(item => item.Matches(query));

    public LibraryWork? Filter(string query, LibraryQualityFilter quality, string? transientPreferredItemId = null)
        => Filter(new LibrarySearchQuery(query), quality, transientPreferredItemId);

    public LibraryWork? Filter(LibrarySearchQuery query, LibraryQualityFilter quality, string? transientPreferredItemId = null, bool localOnly = false)
    {
        // A work's alternative name applies to every edition; author, tempo and edition stay version-specific.
        var workAliases = AllVariants.SelectMany(item => item.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = Variants.Where(item => (!localOnly || item.Available) && item.MatchesQualityFilter(quality))
            .Select(item => (Item: item, Match: query.Match(item, workAliases))).Where(result => result.Match != null)
            .OrderByDescending(result => result.Match!.Value.Score).ToArray();
        var matching = matches.Select(result => result.Item).ToArray();
        if (matching.Length == 0) return null;
        var preferred = matching.FirstOrDefault(item => item.Id == transientPreferredItemId)?.Id
            ?? matching.FirstOrDefault(item => item.Id == SelectedVariant.Id)?.Id ?? matching[0].Id;
        return new LibraryWork(WorkId, matching, preferred, AllVariants, matching.Length < AllVariants.Count, PreferredVariantId,
            matches[0].Match!.Value.Score, matches.First(result => result.Item.Id == preferred).Match!.Value.Hint);
    }
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
    [JsonIgnore]
    public string Description
    {
        get
        {
            var lines = new List<string> { $"{Variant} · {Author}" };
            if (Reference is { } reference)
            {
                if (reference.StartSeconds is double start && double.IsFinite(start) && start >= 0)
                    lines.Add("参考录音定位：" + FormatReferenceTime(start));
                var hasSegmentStart = reference.SegmentStartSeconds is double segmentStart && double.IsFinite(segmentStart) && segmentStart >= 0;
                var hasSegmentEnd = reference.SegmentEndSeconds is double segmentEnd && double.IsFinite(segmentEnd) && segmentEnd >= 0;
                if (hasSegmentStart || hasSegmentEnd)
                {
                    var startLabel = reference.SegmentStartSeconds is double from && double.IsFinite(from) && from >= 0 ? FormatReferenceTime(from) : "未注明";
                    var endLabel = reference.SegmentEndSeconds is double to && double.IsFinite(to) && to >= 0 ? FormatReferenceTime(to) : "未注明";
                    lines.Add($"参考片段范围：{startLabel}–{endLabel}");
                }
            }
            if (DurationSeconds is double duration && double.IsFinite(duration) && duration > 0)
                lines.Add("谱面时长：" + FormatReferenceTime(duration));
            lines.Add(ReviewNote);
            return string.Join("\n", lines);
        }
    }

    private static string FormatReferenceTime(double seconds)
    {
        var minutes = (long)(seconds / 60);
        return $"{minutes:00}:{(seconds - minutes * 60).ToString("00.##", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}

public sealed class ReferenceMetadata
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
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
    [JsonPropertyName("midi_sha256")]
    public string? MidiSha256 { get; set; }
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
    public double LibrarySidebarWidth { get; set; } = 330;
    public LibrarySortOrder LibrarySort { get; set; } = LibrarySortOrder.Smart;
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
