using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harmonica.Core;
using Harmonica.Desktop.Models;

namespace Harmonica.Desktop.Services;

public sealed class LibraryStore
{
    private const int MaxBytes = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string assets;
    private readonly Func<HttpClient> clientFactory;
    private readonly SemaphoreSlim libraryWriteGate = new(1, 1);
    private readonly SemaphoreSlim settingsWriteGate = new(1, 1);
    public string Root { get; }
    public string ScoresDirectory => Path.Combine(Root, "Scores");
    public List<LibraryItem> Items { get; private set; } = [];
    public IReadOnlyDictionary<string, CatalogSong> Catalog { get; private set; } = new Dictionary<string, CatalogSong>();
    public UserSettings Settings { get; private set; } = new();
    public List<string> Warnings { get; } = [];

    public LibraryStore(string root, string assetsDirectory, Func<HttpClient>? clientFactory = null)
    {
        Root = Path.GetFullPath(root);
        assets = assetsDirectory;
        this.clientFactory = clientFactory ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(45) });
        Directory.CreateDirectory(ScoresDirectory);
    }

    public async Task InitializeAsync()
    {
        Settings = ReadJsonOrPreserve<UserSettings>("settings.json") ?? new();
        Settings.Volume = float.IsFinite(Settings.Volume) ? Math.Clamp(Settings.Volume, 0, 1) : .2f;
        if (Settings.GameNoteGapMs is < 0 or > 250) Settings.GameNoteGapMs = 60;
        Settings.Guide ??= new();
        Settings.SelectedVariants ??= new(StringComparer.Ordinal);
        Settings.Guide.Normalize();
        if (string.IsNullOrWhiteSpace(Settings.OverlayHotkey)) Settings.OverlayHotkey = "F7";
        Items = ReadJsonOrPreserve<List<LibraryItem>>("library.json") ?? [];
        // Only a basename may be persisted. The installed application never writes outside Scores.
        foreach (var item in Items)
        {
            if (item.NeedsBpmConfirmation) item.BpmKnown = false;
            if (item.FileName != null && !ValidFileName(item.FileName))
                throw new InvalidDataException("曲库中的文件路径无效，已保留原数据，请检查 library.json。");
            item.Available = item.FileName != null && File.Exists(ScorePath(item));
            if (item.Available)
            {
                var bytes = await File.ReadAllBytesAsync(ScorePath(item));
                if (bytes.LongLength > MaxBytes)
                {
                    item.Available = false;
                    Warnings.Add($"曲谱文件 {item.FileName} 超过 1 MiB，已保留文件且暂不可用。");
                }
                else item.Hash = Hash(bytes);
            }
        }
        using var catalogJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(assets, "catalog.json")));
        var schemaVersion = catalogJson.RootElement.TryGetProperty("schema_version", out var schema)
            ? schema.GetInt32() : 2;
        if (schemaVersion is not (2 or 3)) throw new InvalidDataException("曲谱来源索引版本不受支持。");
        var records = new Dictionary<string, CatalogSong>();
        var songs = catalogJson.RootElement.GetProperty("songs").EnumerateArray()
            .Select(entry => ParseCatalogSong(entry, schemaVersion)).ToList();
        foreach (var workGroup in songs.GroupBy(song => song.WorkId, StringComparer.Ordinal))
        {
            var defaults = workGroup.Where(song => song.IsDefaultVariant).ToArray();
            if (defaults.Length > 1) throw new InvalidDataException("同一作品只能有一个默认版本。");
        }
        foreach (var song in songs)
        {
            records.Add(song.Id, song);
            var linkedItems = Items.Where(item => item.SourceId == song.Id).ToArray();
            if (linkedItems.Length == 0)
            {
                linkedItems = [new LibraryItem
                {
                    Id = "source-" + song.Id, SourceId = song.Id, Title = song.Title, Bpm = song.Bpm,
                    BpmKnown = song.BpmKnown, NeedsBpmConfirmation = !song.BpmKnown,
                    WorkId = song.WorkId, VariantId = song.VariantId, IsDefaultVariant = song.IsDefaultVariant,
                    Acquisition = song.Acquisition, VerifiedSha256 = song.VerifiedSha256, RhythmStatus = song.RhythmStatus,
                    ReviewNote = song.ReviewNote, Reference = song.Reference
                }];
                Items.Add(linkedItems[0]);
            }
            foreach (var item in linkedItems)
            {
                if (!item.Available && !song.BpmKnown)
                {
                    item.Bpm = song.Bpm;
                    item.BpmKnown = false;
                    item.NeedsBpmConfirmation = true;
                }
                else if (!item.Available && song.BpmKnown && !item.BpmKnown)
                {
                    item.Bpm = song.Bpm;
                    item.BpmKnown = true;
                    item.NeedsBpmConfirmation = false;
                }
                item.WorkId = song.WorkId;
                item.VariantId = song.VariantId;
                if (item.VariantLabelOverride is null) item.IsDefaultVariant = song.IsDefaultVariant;
                item.Acquisition = song.Acquisition;
                item.Aliases = song.Aliases;
                item.Variant = item.VariantLabelOverride ?? song.Variant;
                item.SourceAuthor = song.Author;
                item.ReviewNote = song.ReviewNote;
                item.Reference = song.Reference;
                if (!item.CalibrationRevoked)
                {
                    item.VerifiedSha256 = song.VerifiedSha256;
                    item.RhythmStatus = song.RhythmStatus;
                }
                if (!item.Available && song.DurationSeconds is double duration) item.DurationSeconds = duration;
            }
        }
        Catalog = records;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(assets, "Examples"), "*.txt").Order())
        {
            var exampleId = "example-" + Path.GetFileNameWithoutExtension(file);
            if (Items.Any(item => item.Id == exampleId)) continue;
            var fileName = exampleId + ".txt";
            var destination = Path.Combine(ScoresDirectory, fileName);
            var alreadyExists = File.Exists(destination);
            string text;
            ScoreDocument document;
            try
            {
                // A missing/damaged index must never reset a user's edited example.
                text = await ReadScoreTextAsync(alreadyExists ? destination : file);
                document = ScoreCodec.Read(text, fileName);
                ScoreCodec.Parse(document);
            }
            catch (Exception ex) when (alreadyExists && ex is IOException or FormatException or DecoderFallbackException)
            {
                Warnings.Add($"已有示例 {fileName} 无法读取，已保留原文件且未覆盖：{ex.Message}");
                continue;
            }
            var item = new LibraryItem { Id = exampleId, Title = document.Title, Bpm = document.Bpm, Example = true,
                FileName = fileName, Hash = null, Available = true,
                NeedsBpmConfirmation = document.NeedsBpmConfirmation, WorkId = exampleId, VariantId = exampleId,
                IsDefaultVariant = true, Acquisition = "external-import" };
            if (!alreadyExists) await AtomicTextAsync(destination, text, overwrite: false);
            item.Hash = Hash(await File.ReadAllBytesAsync(destination));
            Items.Insert(0, item);
        }
        foreach (var item in Items)
        {
            Catalog.TryGetValue(item.SourceId ?? "", out var linkedSource);
            item.WorkId ??= "";
            item.VariantId ??= "";
            item.Acquisition ??= "";
            item.RhythmStatus ??= "not-reviewed";
            item.ReviewNote ??= "音乐节奏尚未复核。";
            if (string.IsNullOrWhiteSpace(item.WorkId)) item.WorkId = linkedSource?.WorkId ?? item.Id;
            if (string.IsNullOrWhiteSpace(item.VariantId)) item.VariantId = linkedSource?.VariantId ?? item.Id;
            if (string.IsNullOrWhiteSpace(item.Acquisition)) item.Acquisition = linkedSource?.Acquisition ?? "external-import";
            if (item.SourceId == null)
            {
                if (item.VariantId == item.Id) item.IsDefaultVariant = true;
            }
            if (item.VerifiedSha256 is null && linkedSource?.VerifiedSha256 is not null && !item.CalibrationRevoked)
            {
                item.VerifiedSha256 = linkedSource.VerifiedSha256;
                item.RhythmStatus = linkedSource.RhythmStatus;
            }
            if (item.Available && item.VerifiedSha256 is not null && !string.Equals(item.Hash, item.VerifiedSha256, StringComparison.OrdinalIgnoreCase))
            {
                item.CalibrationRevoked = true;
                item.VerifiedSha256 = null;
                item.RhythmStatus = "not-reviewed";
            }
        }
        await SaveLibraryAsync();
    }

    private static CatalogSong ParseCatalogSong(JsonElement entry, int schemaVersion)
    {
        string RequiredString(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidDataException(name) : throw new InvalidDataException(name);
        string? OptionalString(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        long OptionalLong(string name) => entry.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
        bool OptionalBool(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
        string id = RequiredString("id");
        string acquisition = OptionalString("acquisition") ?? "direct-download";
        var hasBpm = entry.TryGetProperty("bpm", out var bpmValue);
        var bpmKnown = hasBpm && bpmValue.ValueKind != JsonValueKind.Null;
        int bpm;
        if (!bpmKnown)
        {
            if (schemaVersion != 3 || acquisition != "external-import")
                throw new InvalidDataException("仅 schema 3 外部导入来源可省略 BPM。");
            bpm = 120;
        }
        else if (bpmValue.ValueKind == JsonValueKind.Number && bpmValue.TryGetInt32(out bpm) && bpm is >= 20 and <= 300)
        {
            // A valid integer BPM was supplied.
        }
        else throw new InvalidDataException("曲谱来源索引 BPM 必须为 20–300 的整数。");
        string? downloadUrl = OptionalString("download_url");
        string? sourceUrl = OptionalString("source_url");
        var reference = entry.TryGetProperty("reference", out var referenceElement) && referenceElement.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<ReferenceMetadata>(referenceElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null;
        var song = new CatalogSong(id, RequiredString("title"), OptionalString("filename") ?? id + ".txt", bpm, OptionalLong("size"), OptionalString("sha256"), downloadUrl, sourceUrl)
        {
            BpmKnown = bpmKnown,
            WorkId = OptionalString("work_id") is { Length: > 0 } workId ? workId : id,
            VariantId = OptionalString("variant_id") is { Length: > 0 } variantId ? variantId : id,
            IsDefaultVariant = schemaVersion == 2 || OptionalBool("is_default_variant"),
            Acquisition = acquisition,
            Aliases = entry.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array
                ? aliases.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString() ?? "").Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [],
            Variant = OptionalString("variant") ?? OptionalString("edition") ?? "来源版本", Version = OptionalString("version") ?? "",
            Author = OptionalString("arranger") ?? "来源未注明", EvidenceUrl = OptionalString("evidence_url"),
            DownloadFormat = OptionalString("download_format") ?? "harmonica-txt", ConvertedSha256 = OptionalString("converted_sha256"),
            DurationSeconds = entry.TryGetProperty("duration_seconds", out var duration) && duration.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0 ? seconds : null,
            RhythmStatus = entry.TryGetProperty("verification", out var verification) && verification.ValueKind == JsonValueKind.Object && verification.TryGetProperty("rhythm", out var rhythm) && rhythm.ValueKind == JsonValueKind.String
                ? rhythm.GetString() ?? "not-reviewed" : "not-reviewed",
            ReviewNote = OptionalString("review_note") ?? "音乐节奏尚未复核。",
            VerifiedSha256 = OptionalString("verified_sha256"),
            Reference = reference
        };
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "id", "title", "filename", "bpm", "size", "sha256", "download_url", "source_url", "work_id", "variant_id",
            "is_default_variant", "acquisition", "aliases", "variant", "edition", "version", "arranger", "evidence_url",
            "download_format", "converted_sha256", "duration_seconds", "verification", "review_note", "verified_sha256", "reference"
        };
        song = song with { ExtraData = entry.EnumerateObject().Where(property => !known.Contains(property.Name)).ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal) };
        if (id.Length == 0 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            song.WorkId.Length == 0 || song.VariantId.Length == 0 || song.BpmKnown && (song.Bpm is < 20 or > 300))
            throw new InvalidDataException("曲谱来源索引字段无效。");
        if (song.VerifiedSha256 is not null && !IsSha256(song.VerifiedSha256)) throw new InvalidDataException("校准索引无效。");
        if (!IsHttps(sourceUrl)) throw new InvalidDataException("曲谱来源索引需要 HTTPS 来源。");
        if (reference?.Url is not null && !IsHttps(reference.Url)) throw new InvalidDataException("参考链接必须使用 HTTPS。");
        if (reference?.SegmentStartSeconds is double start && (!double.IsFinite(start) || start < 0) ||
            reference?.SegmentEndSeconds is double end && (!double.IsFinite(end) || end < 0) ||
            reference?.SegmentStartSeconds is double s && reference.SegmentEndSeconds is double e && e <= s)
            throw new InvalidDataException("参考片段时长无效。");
        if (acquisition == "direct-download")
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Host != "raw.githubusercontent.com" ||
                song.Size is <= 0 or > MaxBytes || !IsSha256(song.Sha256))
                throw new InvalidDataException("曲谱来源索引无效。");
            if (song.DownloadFormat is not ("harmonica-txt" or CommunityScoreConverter.Format) ||
                song.DownloadFormat == CommunityScoreConverter.Format && !IsSha256(song.ConvertedSha256))
                throw new InvalidDataException("曲谱转换索引无效。");
        }
        else if (acquisition == "external-import")
        {
            if (downloadUrl is not null) throw new InvalidDataException("外部来源不能配置直接下载地址。");
        }
        else throw new InvalidDataException("曲谱来源类型无效。");
        if (acquisition == "external-import") ValidateExternalVerification(entry, song, schemaVersion);
        return song;
    }

    private static void ValidateExternalVerification(JsonElement entry, CatalogSong song, int schemaVersion)
    {
        JsonElement verification = default;
        if (entry.TryGetProperty("verification", out var verificationElement))
        {
            if (verificationElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("外部来源校准索引无效。");
            verification = verificationElement;
        }

        foreach (var field in new[] { "app_import", "log_playback", "game" })
        {
            if (verification.ValueKind == JsonValueKind.Object && verification.TryGetProperty(field, out var claim) &&
                (claim.ValueKind != JsonValueKind.String || claim.GetString() is not ("not-tested" or "not-reviewed")))
                throw new InvalidDataException("外部来源不能声明导入、播放或游戏验证已完成。");
        }

        var rhythmStatus = "not-reviewed";
        if (verification.ValueKind == JsonValueKind.Object && verification.TryGetProperty("rhythm", out var rhythm))
        {
            if (rhythm.ValueKind != JsonValueKind.String) throw new InvalidDataException("外部来源节奏校准状态无效。");
            rhythmStatus = rhythm.GetString() ?? "";
        }
        if (rhythmStatus != "reviewed")
        {
            if (rhythmStatus is not ("not-tested" or "not-reviewed")) throw new InvalidDataException("外部来源节奏校准状态无效。");
            return;
        }

        if (schemaVersion != 3 || !IsSha256(song.VerifiedSha256))
            throw new InvalidDataException("已校准的外部来源必须提供校正版 SHA-256。");

        var hasSourceHash = false;
        foreach (var field in new[] { "source_sha256", "source_fingerprint_sha256" })
        {
            if (!entry.TryGetProperty(field, out var value)) continue;
            if (value.ValueKind != JsonValueKind.String || !IsSha256(value.GetString()))
                throw new InvalidDataException("已校准的外部来源原始来源指纹无效。");
            hasSourceHash = true;
        }
        if (!hasSourceHash) throw new InvalidDataException("已校准的外部来源必须绑定原始来源 SHA-256 或指纹。");

        if (!entry.TryGetProperty("reference", out var referenceElement) || referenceElement.ValueKind != JsonValueKind.Object ||
            song.Reference is not { } reference || !IsHttps(reference.Url) || string.IsNullOrWhiteSpace(reference.Edition) ||
            reference.Edition.Length > 200 || !IsSha256(reference.AudioSha256) ||
            !referenceElement.TryGetProperty("start_seconds", out var startElement) || startElement.ValueKind != JsonValueKind.Number ||
            !startElement.TryGetDouble(out var startSeconds) || !double.IsFinite(startSeconds) || startSeconds < 0)
            throw new InvalidDataException("已校准的外部来源必须注明具体参考录音、版本、音频 SHA-256 和起始位置。");
        if (referenceElement.TryGetProperty("score_sha256", out var scoreHash) &&
            (scoreHash.ValueKind != JsonValueKind.String || !IsSha256(scoreHash.GetString())))
            throw new InvalidDataException("参考乐谱 SHA-256 无效。");

        static bool HasReviewText(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()) && value.GetString()!.Length <= 4000;
        if (!HasReviewText(entry, "review_note") || !HasReviewText(entry, "review_method"))
            throw new InvalidDataException("已校准的外部来源必须包含审阅说明和方法。");

        if (!IsStableCatalogId(song.VariantId) || !entry.TryGetProperty("evidence", out var evidence) ||
            evidence.ValueKind != JsonValueKind.Array || evidence.GetArrayLength() != 1)
            throw new InvalidDataException("已校准的外部来源必须包含对应版本的公开证据文件。");
        var proof = evidence[0];
        var expectedProofPath = $"catalog/calibration-evidence/{song.VariantId}.json";
        if (proof.ValueKind != JsonValueKind.Object || !proof.TryGetProperty("file", out var proofFile) ||
            proofFile.ValueKind != JsonValueKind.String || proofFile.GetString() != expectedProofPath ||
            !proof.TryGetProperty("sha256", out var proofHash) || proofHash.ValueKind != JsonValueKind.String ||
            !IsSha256(proofHash.GetString()))
            throw new InvalidDataException("校准证据必须指向对应版本的公开证明文件并提供 SHA-256。");

        if (!entry.TryGetProperty("validation", out var validation) || validation.ValueKind != JsonValueKind.Object ||
            !validation.TryGetProperty("production_parser", out var parser) || parser.ValueKind != JsonValueKind.String ||
            parser.GetString() != "passed" ||
            !validation.TryGetProperty("reviewed_note_count", out var noteCount) || noteCount.ValueKind != JsonValueKind.Number ||
            !noteCount.TryGetInt32(out var reviewedNotes) || reviewedNotes <= 0 ||
            !validation.TryGetProperty("maximum_parser_boundary_error_seconds", out var boundaryError) ||
            boundaryError.ValueKind != JsonValueKind.Number || !boundaryError.TryGetDouble(out var errorSeconds) ||
            !double.IsFinite(errorSeconds) || errorSeconds < 0)
            throw new InvalidDataException("已校准的外部来源需要通过解析器校验、正数音符核对量和有限边界误差。");
        if (validation.TryGetProperty("independent_attack_supported_notes", out var attackCount) &&
            (attackCount.ValueKind != JsonValueKind.Number || !attackCount.TryGetInt32(out var supportedNotes) || supportedNotes <= 0))
            throw new InvalidDataException("独立起音支持音符数无效。");
    }

    private static bool IsStableCatalogId(string value) => value.Length is > 0 and <= 64 &&
        char.IsAsciiLetterOrDigit(value[0]) && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static bool IsHttps(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Host.Length > 0;
    private static bool IsSha256(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

    public IReadOnlyList<LibraryWork> GetWorks() => Items
        .GroupBy(item => string.IsNullOrWhiteSpace(item.WorkId) ? item.Id : item.WorkId, StringComparer.Ordinal)
        .Select(group => new LibraryWork(group.Key, group, Settings.SelectedVariants.TryGetValue(group.Key, out var selectedId) ? selectedId : null))
        .ToArray();

    private T? ReadJsonOrPreserve<T>(string name)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch (JsonException)
        {
            var backup = path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..6];
            File.Copy(path, backup, false);
            Warnings.Add($"{name} 无法读取，原文件已保留为 {Path.GetFileName(backup)}。");
            return default;
        }
    }

    public string ScorePath(LibraryItem item) => item.FileName != null && ValidFileName(item.FileName)
        ? Path.Combine(ScoresDirectory, item.FileName) : throw new InvalidDataException("曲谱尚未下载。");
    private static bool ValidFileName(string name) => !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains(':') && name != "." && name != "..";
    public async Task<ScoreDocument> ReadDocumentAsync(LibraryItem item)
    {
        var path = ScorePath(item);
        var text = await ReadScoreTextAsync(path);
        var actualHash = Hash(await File.ReadAllBytesAsync(path));
        if (!string.Equals(item.Hash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            item.Hash = actualHash;
            if (item.VerifiedSha256 is not null && !string.Equals(actualHash, item.VerifiedSha256, StringComparison.OrdinalIgnoreCase))
            {
                item.CalibrationRevoked = true;
                item.VerifiedSha256 = null;
                item.RhythmStatus = "not-reviewed";
            }
            await SaveLibraryAsync();
        }
        var doc = ScoreCodec.Read(text, item.Title + " bpm" + item.Bpm);
        item.DurationSeconds = ScoreCodec.Parse(doc).DurationSeconds;
        return doc with { Title = item.Title, NeedsBpmConfirmation = item.NeedsBpmConfirmation };
    }
    public static async Task<string> ReadScoreTextAsync(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        return await File.ReadAllTextAsync(path, new UTF8Encoding(false, true));
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public async Task<LibraryItem> ImportAsync(string path)
    {
        var text = await ReadScoreTextAsync(path);
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        return await ImportTextCoreAsync(text, Path.GetFileName(path), bytes);
    }
    public async Task<LibraryItem> ImportTextAsync(string text, string name = "粘贴的曲谱", string? originalHash = null)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        var bytes = Encoding.UTF8.GetBytes(text);
        if (originalHash != null && !string.Equals(originalHash, Hash(bytes), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("导入文本与提供的校验值不一致。");
        return await ImportTextCoreAsync(text, name, bytes);
    }

    private async Task<LibraryItem> ImportTextCoreAsync(string text, string name, byte[] bytes)
    {
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        var doc = ScoreCodec.Read(text, name);
        var parsed = ScoreCodec.Parse(doc);
        var hash = Hash(bytes);
        var existing = Items.FirstOrDefault(item => item.Available && string.Equals(item.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var source = Catalog.Values.FirstOrDefault(song => new[] { song.VerifiedSha256, song.ConvertedSha256, song.Sha256 }
            .Any(expected => expected != null && string.Equals(expected, hash, StringComparison.OrdinalIgnoreCase)));
        var item = source == null ? NewPersonalItem(doc.Title, doc.Bpm, !doc.NeedsBpmConfirmation) : await FindOrCreateCatalogItemAsync(source);
        var fileName = item.Id + ".txt";
        await AtomicBytesAsync(Path.Combine(ScoresDirectory, fileName), bytes, overwrite: false);
        item.FileName = fileName;
        item.Hash = hash;
        item.Available = true;
        item.DurationSeconds = parsed.DurationSeconds;
        if (source == null)
        {
            item.Title = doc.Title;
            item.Bpm = doc.Bpm;
            item.BpmKnown = !doc.NeedsBpmConfirmation;
            item.NeedsBpmConfirmation = doc.NeedsBpmConfirmation;
        }
        else
        {
            item.Title = source.Title;
            ApplyImportedBpm(item, source, doc);
            ApplyCatalogMetadata(item, source);
            SetCalibrationForActualHash(item, source, hash);
        }
        if (!Items.Contains(item)) Items.Insert(0, item);
        await SaveLibraryAsync();
        return item;
    }

    public async Task<LibraryItem> ImportSourceAsync(string path, string sourceId)
    {
        if (!Catalog.TryGetValue(sourceId, out var source)) throw new InvalidDataException("找不到所选来源。");
        if (source.Acquisition != "external-import") throw new InvalidDataException("此版本使用固定来源下载。");
        var text = await ReadScoreTextAsync(path);
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        return await ImportCatalogTextAsync(source, text, Path.GetFileName(path), requireVerifiedHash: false, bytes: bytes);
    }

    public async Task<LibraryItem> ImportReviewedAsync(string path, string sourceId, string workId, string variantId)
    {
        if (!Catalog.TryGetValue(sourceId, out var source)) throw new InvalidDataException("找不到要校准的来源版本。");
        if (!string.Equals(source.WorkId, workId, StringComparison.Ordinal) || !string.Equals(source.VariantId, variantId, StringComparison.Ordinal))
            throw new InvalidDataException("来源、作品和版本信息不匹配。");
        if (!string.Equals(source.RhythmStatus, "reviewed", StringComparison.Ordinal)) throw new InvalidDataException("此版本没有通过带证据的节奏校准审阅。");
        if (source.VerifiedSha256 is null) throw new InvalidDataException("此版本没有可用于校验的 SHA-256。");
        var text = await ReadScoreTextAsync(path);
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        return await ImportCatalogTextAsync(source, text, Path.GetFileName(path), requireVerifiedHash: true, bytes: bytes);
    }

    private async Task<LibraryItem> ImportCatalogTextAsync(CatalogSong source, string text, string name, bool requireVerifiedHash, byte[] bytes)
    {
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        var doc = ScoreCodec.Read(text, name);
        var parsed = ScoreCodec.Parse(doc);
        var hash = Hash(bytes);
        if (source.VerifiedSha256 is not null && !string.Equals(hash, source.VerifiedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("文件校验值与指定版本不一致，未导入或覆盖任何文件。");
        if (requireVerifiedHash && source.VerifiedSha256 is null) throw new InvalidDataException("此版本没有可用于校验的 SHA-256。");
        if (source.BpmKnown && !doc.NeedsBpmConfirmation && doc.Bpm != source.Bpm) throw new InvalidDataException("导入文件的速度与所选来源版本不一致。");
        var duplicate = Items.FirstOrDefault(item => item.Available && item.SourceId == source.Id &&
            string.Equals(item.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            ApplyImportedBpm(duplicate, source, doc);
            ApplyCatalogMetadata(duplicate, source);
            SetCalibrationForActualHash(duplicate, source, hash);
            await SaveLibraryAsync();
            return duplicate;
        }
        var item = await FindOrCreateCatalogItemAsync(source);
        var fileName = item.Id + ".txt";
        await AtomicBytesAsync(Path.Combine(ScoresDirectory, fileName), bytes, overwrite: false);
        item.FileName = fileName;
        item.Title = source.Title;
        ApplyImportedBpm(item, source, doc);
        item.Hash = hash;
        item.Available = true;
        item.DurationSeconds = parsed.DurationSeconds;
        item.CalibrationRevoked = false;
        ApplyCatalogMetadata(item, source);
        SetCalibrationForActualHash(item, source, hash);
        if (!Items.Contains(item)) Items.Insert(0, item);
        await SaveLibraryAsync();
        return item;
    }

    private Task<LibraryItem> FindOrCreateCatalogItemAsync(CatalogSong source)
    {
        var reusable = Items.FirstOrDefault(item => item.SourceId == source.Id && !item.Available &&
            (item.FileName == null || !File.Exists(Path.Combine(ScoresDirectory, item.FileName))));
        if (reusable != null) return Task.FromResult(reusable);
        if (Items.Any(item => item.SourceId == source.Id && item.Available))
        {
            var copy = NewCatalogItem(source);
            copy.Id = "source-copy-" + Guid.NewGuid().ToString("N");
            copy.IsDefaultVariant = false;
            copy.VariantLabelOverride = source.Variant + "（本机导入）";
            copy.Variant = copy.VariantLabelOverride;
            Items.Insert(0, copy);
            return Task.FromResult(copy);
        }
        var item = NewCatalogItem(source);
        Items.Add(item);
        return Task.FromResult(item);
    }

    private static LibraryItem NewCatalogItem(CatalogSong source) => new()
    {
        Id = "source-" + source.Id, SourceId = source.Id, Title = source.Title, Bpm = source.Bpm,
        BpmKnown = source.BpmKnown, NeedsBpmConfirmation = !source.BpmKnown,
        WorkId = source.WorkId, VariantId = source.VariantId, IsDefaultVariant = source.IsDefaultVariant,
        Acquisition = source.Acquisition, VerifiedSha256 = source.VerifiedSha256, RhythmStatus = source.RhythmStatus,
        ReviewNote = source.ReviewNote, Reference = source.Reference, Variant = source.Variant, SourceAuthor = source.Author,
        DurationSeconds = source.DurationSeconds
    };

    private static LibraryItem NewPersonalItem(string title, int bpm, bool bpmKnown = true)
    {
        var item = new LibraryItem { Title = title, Bpm = bpm, BpmKnown = bpmKnown, NeedsBpmConfirmation = !bpmKnown, Acquisition = "external-import", IsDefaultVariant = true };
        item.WorkId = item.Id;
        item.VariantId = item.Id;
        return item;
    }

    private static void ApplyImportedBpm(LibraryItem item, CatalogSong source, ScoreDocument document)
    {
        if (source.BpmKnown)
        {
            item.Bpm = source.Bpm;
            item.BpmKnown = true;
            item.NeedsBpmConfirmation = false;
        }
        else
        {
            item.Bpm = document.Bpm;
            item.BpmKnown = !document.NeedsBpmConfirmation;
            item.NeedsBpmConfirmation = document.NeedsBpmConfirmation;
        }
    }

    private static void ApplyCatalogMetadata(LibraryItem item, CatalogSong source)
    {
        item.SourceId = source.Id;
        item.WorkId = source.WorkId;
        item.VariantId = source.VariantId;
        item.Acquisition = source.Acquisition;
        item.Aliases = source.Aliases;
        item.Variant = item.VariantLabelOverride ?? source.Variant;
        item.SourceAuthor = source.Author;
        item.Reference = source.Reference;
        item.ReviewNote = source.ReviewNote;
        if (!item.CalibrationRevoked)
        {
            item.VerifiedSha256 = source.VerifiedSha256;
            item.RhythmStatus = source.RhythmStatus;
        }
    }

    private static void SetCalibrationForActualHash(LibraryItem item, CatalogSong source, string actualHash)
    {
        if (source.VerifiedSha256 is null) return;
        if (string.Equals(actualHash, source.VerifiedSha256, StringComparison.OrdinalIgnoreCase))
        {
            item.VerifiedSha256 = source.VerifiedSha256;
            item.RhythmStatus = source.RhythmStatus;
            item.CalibrationRevoked = false;
        }
        else
        {
            item.VerifiedSha256 = null;
            item.RhythmStatus = "not-reviewed";
            item.CalibrationRevoked = true;
        }
    }

    public async Task DownloadAsync(LibraryItem item, CancellationToken token)
    {
        if (item.Available) return;
        if (item.SourceId == null || !Catalog.TryGetValue(item.SourceId, out var source)) throw new InvalidDataException("找不到该曲谱的来源。");
        if (source.Acquisition != "direct-download" || source.DownloadUrl is null)
            throw new InvalidDataException("此版本需要从来源自行导入，不能直接下载。");
        using var client = clientFactory();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HarmonicaDesk/0.2");
        using var response = await client.GetAsync(source.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https" || response.RequestMessage?.RequestUri?.Host != "raw.githubusercontent.com") throw new InvalidDataException("下载跳转到了非预期来源。");
        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("下载文件超出大小限制。");
        using var memory = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (memory.Length + count > MaxBytes) throw new InvalidDataException("下载文件超出大小限制。");
            memory.Write(buffer, 0, count);
        }
        var bytes = memory.ToArray();
        if (bytes.LongLength != source.Size || !string.Equals(Hash(bytes), source.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件校验失败：下载内容与固定来源不一致，未保存。");
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (source.DownloadFormat == CommunityScoreConverter.Format)
        {
            text = CommunityScoreConverter.ToText(text);
            if (!string.Equals(Hash(Encoding.UTF8.GetBytes(text)), source.ConvertedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("本地转换结果与已验证版本不一致，未保存。");
        }
        var doc = ScoreCodec.Read(text, source.FileName);
        var parsed = ScoreCodec.Parse(doc);
        if (doc.Bpm != source.Bpm || source.DurationSeconds is double expectedSeconds && Math.Abs(parsed.DurationSeconds - expectedSeconds) > .01)
            throw new InvalidDataException("曲谱速度或时长与已验证索引不一致，未保存。");
        token.ThrowIfCancellationRequested();
        var path = Path.Combine(ScoresDirectory, item.Id + ".txt");
        await AtomicTextAsync(path, text, overwrite: false);
        item.FileName = Path.GetFileName(path);
        item.Hash = Hash(Encoding.UTF8.GetBytes(text));
        item.Available = true;
        item.DurationSeconds = parsed.DurationSeconds;
        item.CalibrationRevoked = false;
        ApplyCatalogMetadata(item, source);
        SetCalibrationForActualHash(item, source, item.Hash);
        await SaveLibraryAsync();
    }

    public async Task SaveDocumentAsync(LibraryItem item, ScoreDocument doc)
    {
        var parsed = ScoreCodec.Parse(doc);
        var text = ScoreCodec.Write(doc with { NeedsBpmConfirmation = false, Legacy = false });
        var fileName = item.FileName ?? item.Id + ".txt";
        if (!ValidFileName(fileName)) throw new InvalidDataException("无效文件名。");
        var path = Path.Combine(ScoresDirectory, fileName);
        await AtomicTextAsync(path, text, overwrite: Items.Contains(item));
        item.FileName = fileName;
        item.Title = doc.Title;
        item.Bpm = doc.Bpm;
        item.BpmKnown = true;
        item.NeedsBpmConfirmation = false;
        item.Hash = Hash(Encoding.UTF8.GetBytes(text));
        item.Available = true;
        item.DurationSeconds = parsed.DurationSeconds;
        if (string.IsNullOrWhiteSpace(item.WorkId)) item.WorkId = item.Id;
        if (string.IsNullOrWhiteSpace(item.VariantId)) item.VariantId = item.Id;
        if (string.IsNullOrWhiteSpace(item.Acquisition)) item.Acquisition = "external-import";
        if (item.VerifiedSha256 is not null && !string.Equals(item.Hash, item.VerifiedSha256, StringComparison.OrdinalIgnoreCase))
        {
            item.CalibrationRevoked = true;
            item.VerifiedSha256 = null;
            item.RhythmStatus = "not-reviewed";
        }
        await SaveLibraryAsync();
    }
    public async Task<LibraryItem> SaveCopyAsync(ScoreDocument doc)
    {
        var item = NewPersonalItem(doc.Title, doc.Bpm, !doc.NeedsBpmConfirmation);
        await SaveDocumentAsync(item, doc);
        Items.Insert(0, item);
        await SaveLibraryAsync();
        return item;
    }
    public async Task SaveLibraryAsync()
    {
        await libraryWriteGate.WaitAsync();
        try
        {
            // Take the snapshot after acquiring the gate, never before an older writer finishes.
            await AtomicTextAsync(Path.Combine(Root, "library.json"), JsonSerializer.Serialize(Items, JsonOptions));
        }
        finally { libraryWriteGate.Release(); }
    }

    public async Task SaveSettingsAsync()
    {
        await settingsWriteGate.WaitAsync();
        try
        {
            await AtomicTextAsync(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(Settings, JsonOptions));
        }
        finally { settingsWriteGate.Release(); }
    }

    public static async Task AtomicTextAsync(string path, string text, bool overwrite = true)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task AtomicBytesAsync(string path, byte[] bytes, bool overwrite)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, path, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static IReadOnlyList<string> EnumerateImportFiles(string directory)
    {
        var roots = Directory.Exists(Path.Combine(directory, "local"))
            ? new[] { Path.Combine(directory, "local", "scores"), Path.Combine(directory, "local", "my-scores") }.Where(Directory.Exists)
            : new[] { directory }.AsEnumerable();
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 8, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var files = roots.SelectMany(root => Directory.EnumerateFiles(root, "*.txt", options)).Take(501).ToArray();
        if (files.Length > 500) throw new InvalidDataException("一次最多导入 500 首，请选择更小的曲谱文件夹。");
        return files;
    }
}
