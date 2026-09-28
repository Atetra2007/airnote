#:project ../src/Harmonica.Desktop/Harmonica.Desktop.csproj
#:project ../src/Harmonica.Windows/Harmonica.Windows.csproj
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property RuntimeIdentifier=win-x64
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Harmonica.Core;
using Harmonica.Desktop.Services;
using Harmonica.Windows;

// Local audit only. A fresh output directory is required; production profiles are never opened.
if (args.Length != 2) throw new ArgumentException("Audit-SelectedScores.cs REPOSITORY NEW_LOCAL_OUTPUT_DIRECTORY");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var localRoot = Path.Combine(root, "local") + Path.DirectorySeparatorChar;
if (!output.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
    throw new IOException("Use a new directory inside the repository local directory.");
Directory.CreateDirectory(output);
var assets = Path.Combine(output, "Resources");
Directory.CreateDirectory(Path.Combine(assets, "Examples"));
File.Copy(Path.Combine(root, "catalog/songs.json"), Path.Combine(assets, "catalog.json"));
foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "scores/original"), "*.txt"))
    File.Copy(path, Path.Combine(assets, "Examples", Path.GetFileName(path)));
var profile = Path.Combine(output, "profile");
var store = new LibraryStore(profile, assets, () => throw new Exception("Network is forbidden in this audit"));
await store.InitializeAsync();
var catalogUnknown = store.Catalog.Values.Count(s => !s.BpmKnown);
using var ledger = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "local/catalog-0.4.0/coverage-ledger.json")));
string[] titles = ["虫儿飞", "晴天", "雨爱", "蒲公英的约定"];
var results = new List<object>();
var imported = new List<(string Id, string Hash)>();
foreach (var title in titles)
{
    var calibration = ledger.RootElement.GetProperty("calibrations").EnumerateArray().Single(c =>
        c.GetProperty("title").GetString() == title && c.GetProperty("status").GetString() == "reviewed");
    var id = calibration.GetProperty("variant_id").GetString()!;
    var path = Path.Combine(root, calibration.GetProperty("file").GetString()!);
    var expectedHash = store.Catalog[id].VerifiedSha256!;
    if (Hash(path) != expectedHash) throw new InvalidDataException(title + " score hash mismatch");
    var evidence = calibration.GetProperty("evidence").EnumerateArray().Select(e =>
    {
        var file = e.GetProperty("file").GetString()!;
        var full = Path.Combine(root, file);
        return new { file, exists = File.Exists(full), hash_matches = File.Exists(full) && Hash(full) == e.GetProperty("sha256").GetString() };
    }).ToArray();
    var source = store.Catalog[id];
    var item = await store.ImportReviewedAsync(path, id, source.WorkId, source.VariantId);
    if (!item.IsCalibrated) throw new InvalidDataException(title + " lost its verified calibration");
    var score = ScoreCodec.Parse(await store.ReadDocumentAsync(item));
    if (Math.Abs(score.DurationSeconds - source.DurationSeconds!.Value) > .001)
        throw new InvalidDataException(title + " duration changed");
    var checks = await Simulate(score);
    imported.Add((id, expectedHash));
    double elapsed = 0;
    var events = score.Events.Select((n, index) =>
    {
        var duration = n.Beats * 60d / score.Document.Bpm;
        var start = elapsed; elapsed += duration;
        int[] steps = [0, 2, 4, 5, 7, 9, 11];
        return new { event_number = index + 1, midi = n.Degree == 0 ? (int?)null : 60 + n.Octave * 12 + steps[n.Degree - 1] + (n.Sharp ? 1 : 0), start_seconds = start, duration_seconds = duration, end_seconds = elapsed, n.Beats };
    }).ToArray();
    results.Add(new { title, variant_id = id, sha256 = expectedHash, source_file = calibration.GetProperty("file").GetString(), reference = calibration.GetProperty("reference").Clone(), duration_seconds = score.DurationSeconds, bpm = score.Document.Bpm, gap_ms = score.Document.GapMs, evidence, simulation = checks, events });
}
// Import the existing game adaptation as a personal, unreviewed score into this isolated profile.
var turkishPath = Path.Combine(root, "local/my-scores/土耳其进行曲（游戏适配版）.txt");
object? turkish = null;
if (File.Exists(turkishPath))
{
    const string expected = "27c3657bcbfef0bb761c169f3ba2295716e3f77f7ff2dc477986273b7e8ea145";
    if (Hash(turkishPath) != expected) throw new InvalidDataException("Turkish adaptation has changed; preserve it for review");
    var item = await store.ImportAsync(turkishPath);
    var score = ScoreCodec.Parse(await store.ReadDocumentAsync(item));
    if (item.IsCalibrated) throw new InvalidDataException("Game adaptation must not be labelled musically reviewed");
    turkish = new { sha256 = expected, duration_seconds = score.DurationSeconds, event_count = score.Events.Count, note_count = score.Events.Count(n => n.Degree != 0), simulation = await Simulate(score), isolated_import = true, musical_review = false };
}
var restarted = new LibraryStore(profile, assets, () => throw new Exception("Network is forbidden in this audit"));
await restarted.InitializeAsync();
foreach (var (id, hash) in imported)
{
    var item = restarted.Items.Single(i => i.SourceId == id && i.Available);
    if (!item.IsCalibrated || item.Hash != hash) throw new InvalidDataException(id + " failed restart validation");
}
var report = new { schema_version = 1, catalog_sha256 = Hash(Path.Combine(assets, "catalog.json")), catalog_unknown_bpm = catalogUnknown,
    isolated_import_restart = "passed", scope = "Production parser, evidence hashes, isolated imports and virtual playback. No live sound, game input or human listening.",
    reviewed_scores = results, turkish_adaptation = turkish, human_listening = "pending", real_game = "not-tested" };
File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Audited {results.Count} reviewed excerpts; isolated import/restart passed. Catalog unknown BPM: {catalogUnknown}. Output: {output}");

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static async Task<object[]> Simulate(ParsedScore score)
{
    var checks = new List<object>();
    foreach (var gap in new[] { 0, 60, 250 })
    {
        var issues = PlaybackEngine.InspectGameNotes(score, gap);
        if (issues.Count > 0) { checks.Add(new { gap_ms = gap, status = "blocked-before-input", issues }); continue; }
        var clock = new AuditClock(); var output = new AuditOutput(clock);
        await using var engine = new PlaybackEngine(() => output, clock);
        await engine.PlayAsync(score, PlaybackMode.Game, gameGapMs: gap);
        if (output.Notes != score.Events.Count(n => n.Degree != 0) || !output.Released)
            throw new InvalidOperationException("Virtual playback lost notes or failed cleanup");
        checks.Add(new { gap_ms = gap, status = "virtual-playback-passed", note_count = output.Notes, elapsed_seconds = clock.Seconds, minimum_hold_ms = output.MinimumHold * 1000 });
    }
    return checks.ToArray();
}
sealed class AuditClock : IPlaybackClock
{
    public double Seconds { get; set; }
    public Task DelayAsync(TimeSpan delay, CancellationToken token) { token.ThrowIfCancellationRequested(); Seconds += Math.Max(.0000001, delay.TotalSeconds); return Task.CompletedTask; }
}
sealed class AuditOutput(AuditClock clock) : IPlaybackOutput
{
    public int Notes; public bool Released; public double MinimumHold = double.MaxValue;
    private double? noteOn;
    public Task BeginAsync(PlaybackMode mode, CancellationToken token) => Task.CompletedTask;
    public Task NoteOnAsync(ScoreEvent note, float volume, CancellationToken token)
    {
        if (GameKeyMapping.ForNote(note).Any(k => k.Mouse)) clock.Seconds += .024;
        noteOn = clock.Seconds; Notes++; return Task.CompletedTask;
    }
    public Task SilenceAsync(CancellationToken token) { if (noteOn is double time) MinimumHold = Math.Min(MinimumHold, clock.Seconds - time); noteOn = null; return Task.CompletedTask; }
    public void CheckSafety() { }
    public void SetVolume(float volume) { }
    public Task ReleaseAsync(CancellationToken token) { Released = true; return Task.CompletedTask; }
    public void Dispose() { }
}
