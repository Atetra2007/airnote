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
        Items = ReadJsonOrPreserve<List<LibraryItem>>("library.json") ?? [];
        // Only a basename may be persisted. The installed application never writes outside Scores.
        foreach (var item in Items)
        {
            if (item.FileName != null && !ValidFileName(item.FileName))
                throw new InvalidDataException("曲库中的文件路径无效，已保留原数据，请检查 library.json。");
            item.Available = item.FileName != null && File.Exists(ScorePath(item));
        }
        using var catalogJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(assets, "catalog.json")));
        var records = new Dictionary<string, CatalogSong>();
        foreach (var entry in catalogJson.RootElement.GetProperty("songs").EnumerateArray())
        {
            string Get(string name) => entry.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
            string? Optional(string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var song = new CatalogSong(Get("id"), Get("title"), Get("filename"), entry.GetProperty("bpm").GetInt32(), entry.GetProperty("size").GetInt64(), Get("sha256"), Get("download_url"), Get("source_url"))
            {
                Aliases = entry.TryGetProperty("aliases", out var aliases) ? aliases.EnumerateArray().Select(a => a.GetString() ?? "").Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [],
                Variant = Optional("variant") ?? "来源版本", Version = Optional("version") ?? "",
                Author = Optional("arranger") ?? "来源未注明", EvidenceUrl = Optional("evidence_url"),
                DownloadFormat = Optional("download_format") ?? "harmonica-txt", ConvertedSha256 = Optional("converted_sha256"),
                DurationSeconds = entry.TryGetProperty("duration_seconds", out var duration) && duration.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0 ? seconds : null,
                RhythmStatus = entry.TryGetProperty("verification", out var verification) && verification.TryGetProperty("rhythm", out var rhythm) ? rhythm.GetString() ?? "not-reviewed" : "not-reviewed",
                ReviewNote = Optional("review_note") ?? "音乐节奏尚未复核。旧版日志记录与本程序验证分别保存。"
            };
            if (!Uri.TryCreate(song.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "raw.githubusercontent.com" || song.Size is <= 0 or > MaxBytes)
                throw new InvalidDataException("曲谱来源索引无效。");
            if (song.DownloadFormat is not ("harmonica-txt" or CommunityScoreConverter.Format) ||
                song.DownloadFormat == CommunityScoreConverter.Format && (song.ConvertedSha256?.Length != 64 || !song.ConvertedSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("曲谱转换索引无效。");
            records.Add(song.Id, song);
            if (!Items.Any(item => item.SourceId == song.Id))
                Items.Add(new LibraryItem { Id = "source-" + song.Id, SourceId = song.Id, Title = song.Title, Bpm = song.Bpm });
            foreach (var item in Items.Where(item => item.SourceId == song.Id))
            {
                item.Aliases = song.Aliases;
                item.Variant = song.Variant;
                item.SourceAuthor = song.Author;
                if (!item.Available) item.DurationSeconds = song.DurationSeconds;
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
                FileName = fileName, Hash = Hash(Encoding.UTF8.GetBytes(text)), Available = true,
                NeedsBpmConfirmation = document.NeedsBpmConfirmation };
            if (!alreadyExists) await AtomicTextAsync(destination, text, overwrite: false);
            Items.Insert(0, item);
        }
        await SaveLibraryAsync();
    }

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
        var doc = ScoreCodec.Read(await ReadScoreTextAsync(ScorePath(item)), item.Title + " bpm" + item.Bpm);
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
        var rawBytes = await File.ReadAllBytesAsync(path);
        return await ImportTextAsync(text, Path.GetFileName(path), Hash(rawBytes));
    }
    public async Task<LibraryItem> ImportTextAsync(string text, string name = "粘贴的曲谱", string? originalHash = null)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("单首曲谱不能超过 1 MiB。");
        var doc = ScoreCodec.Read(text, name);
        var parsed = ScoreCodec.Parse(doc);
        var hash = originalHash ?? Hash(Encoding.UTF8.GetBytes(text));
        var existing = Items.FirstOrDefault(item => item.Available && item.Hash == hash);
        if (existing != null) return existing;
        var source = Catalog.Values.FirstOrDefault(song => (song.ConvertedSha256 ?? song.Sha256) == hash);
        var item = source == null ? null : Items.FirstOrDefault(i => i.SourceId == source.Id && !i.Available);
        var isNew = item == null;
        item ??= new LibraryItem();
        var fileName = item.Id + ".txt";
        await AtomicTextAsync(Path.Combine(ScoresDirectory, fileName), text);
        item.FileName = fileName;
        item.Title = doc.Title;
        item.Bpm = doc.Bpm;
        item.NeedsBpmConfirmation = doc.NeedsBpmConfirmation;
        item.Hash = hash;
        item.Available = true;
        item.DurationSeconds = parsed.DurationSeconds;
        if (isNew) Items.Insert(0, item);
        await SaveLibraryAsync();
        return item;
    }

    public async Task DownloadAsync(LibraryItem item, CancellationToken token)
    {
        if (item.Available) return;
        if (item.SourceId == null || !Catalog.TryGetValue(item.SourceId, out var source)) throw new InvalidDataException("找不到该曲谱的来源。");
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
        if (bytes.LongLength != source.Size || Hash(bytes) != source.Sha256) throw new InvalidDataException("文件校验失败：下载内容与固定来源不一致，未保存。");
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (source.DownloadFormat == CommunityScoreConverter.Format)
        {
            text = CommunityScoreConverter.ToText(text);
            if (Hash(Encoding.UTF8.GetBytes(text)) != source.ConvertedSha256) throw new InvalidDataException("本地转换结果与已验证版本不一致，未保存。");
        }
        var doc = ScoreCodec.Read(text, source.FileName);
        var parsed = ScoreCodec.Parse(doc);
        if (doc.Bpm != source.Bpm || source.DurationSeconds is double expectedSeconds && Math.Abs(parsed.DurationSeconds - expectedSeconds) > .01)
            throw new InvalidDataException("曲谱速度或时长与已验证索引不一致，未保存。");
        token.ThrowIfCancellationRequested();
        var path = Path.Combine(ScoresDirectory, item.Id + ".txt");
        await AtomicTextAsync(path, text, overwrite: false);
        item.FileName = Path.GetFileName(path);
        item.Hash = source.ConvertedSha256 ?? source.Sha256;
        item.Available = true;
        item.DurationSeconds = parsed.DurationSeconds;
        await SaveLibraryAsync();
    }

    public async Task SaveDocumentAsync(LibraryItem item, ScoreDocument doc)
    {
        var parsed = ScoreCodec.Parse(doc);
        var text = ScoreCodec.Write(doc with { NeedsBpmConfirmation = false, Legacy = false });
        var fileName = item.FileName ?? item.Id + ".txt";
        if (!ValidFileName(fileName)) throw new InvalidDataException("无效文件名。");
        await AtomicTextAsync(Path.Combine(ScoresDirectory, fileName), text);
        item.FileName = fileName;
        item.Title = doc.Title;
        item.Bpm = doc.Bpm;
        item.NeedsBpmConfirmation = false;
        item.Hash = Hash(Encoding.UTF8.GetBytes(text));
        item.Available = true;
        item.DurationSeconds = parsed.DurationSeconds;
        await SaveLibraryAsync();
    }
    public async Task<LibraryItem> SaveCopyAsync(ScoreDocument doc)
    {
        var item = new LibraryItem();
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
