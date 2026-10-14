#:project ../src/Harmonica.Desktop/Harmonica.Desktop.csproj
#:project ../src/Harmonica.Windows/Harmonica.Windows.csproj
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property RuntimeIdentifier=win-x64
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Harmonica.Core;
using Harmonica.Desktop.Services;
using Harmonica.Windows;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

if (args.Length != 3)
    throw new ArgumentException("Audit-CatalogBatch.cs REPOSITORY MANIFEST_JSON NEW_LOCAL_OUTPUT_DIRECTORY");

var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Repository root does not exist: " + root);
var manifestPath = ResolveInputPath(root, args[1], "manifest");
var output = Path.GetFullPath(args[2]);
var outputRoot = Path.Combine(root, "local");
ValidateNewOutput(root, outputRoot, output);

var manifestBytes = File.ReadAllBytes(manifestPath);
var manifestHash = Hash(manifestBytes);
using var manifestDocument = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(manifestBytes));
if (manifestDocument.RootElement.ValueKind != JsonValueKind.Object ||
    !manifestDocument.RootElement.TryGetProperty("items", out var manifestItems) ||
    manifestItems.ValueKind != JsonValueKind.Array)
    throw new InvalidDataException("Manifest must be a JSON object with an items array.");

var catalogPath = Path.Combine(root, "catalog", "songs.json");
var catalogBytes = File.ReadAllBytes(catalogPath);
var catalogHash = Hash(catalogBytes);
using var catalogDocument = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(catalogBytes));

Directory.CreateDirectory(output);
Directory.CreateDirectory(Path.Combine(output, "items"));
var assets = Path.Combine(output, "Resources");
Directory.CreateDirectory(Path.Combine(assets, "Examples"));
// The isolated profile receives no catalog claims, so an import can never inherit calibration metadata.
File.WriteAllText(Path.Combine(assets, "catalog.json"), "{\"schema_version\":2,\"songs\":[]}", new UTF8Encoding(false));
File.WriteAllBytes(Path.Combine(output, "manifest.json"), manifestBytes);
var profile = Path.Combine(output, "profile");
var store = new LibraryStore(profile, assets, () => throw new InvalidOperationException("Network access is forbidden in this audit."));
await store.InitializeAsync();

var usedVariantIds = new HashSet<string>(StringComparer.Ordinal);
var results = new List<ItemState>();
var imported = new List<PendingImport>();
var itemNumber = 0;
foreach (var itemElement in manifestItems.EnumerateArray())
{
    itemNumber++;
    var state = new ItemState(itemNumber, itemElement.Clone());
    results.Add(state);
    try
    {
        if (itemElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Manifest item must be a JSON object.");

        state.VariantId = RequiredString(itemElement, "variant_id");
        state.Title = RequiredString(itemElement, "title");
        state.BpmKnown = RequiredBoolean(itemElement, "bpm_known");
        state.SourcePath = OptionalString(itemElement, "path");
        state.Reference = itemElement.TryGetProperty("reference", out var reference) ? reference.Clone() : null;
        if (!usedVariantIds.Add(state.VariantId))
            throw new InvalidDataException("Duplicate variant_id in manifest: " + state.VariantId);
        // Missing source rows in the inventory may also omit their hash claim. Classify the absent
        // input first so it remains a blocked missing-input state instead of a malformed-hash failure.
        if (string.IsNullOrWhiteSpace(state.SourcePath))
            throw new FileNotFoundException("path is missing; this auditor accepts only a verified TXT source or TXT conversion copy.");

        var expectedHash = RequiredString(itemElement, "expected_sha256");
        if (!IsSha256(expectedHash)) throw new InvalidDataException("expected_sha256 must contain 64 hexadecimal characters.");
        state.ExpectedSha256 = expectedHash.ToLowerInvariant();

        var sourcePath = ResolveInputPath(root, state.SourcePath, "score");
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists) throw new FileNotFoundException("Score file does not exist.", sourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported input format: only original TXT or an already-verified converted TXT copy is accepted.");
        if (sourceInfo.Length > AuditPolicy.MaximumScoreBytes) throw new InvalidDataException("Score file exceeds the 1 MiB production limit.");
        var bytes = File.ReadAllBytes(sourcePath);
        state.ActualSha256 = Hash(bytes);
        state.HashMatches = string.Equals(state.ExpectedSha256, state.ActualSha256, StringComparison.OrdinalIgnoreCase);
        if (state.HashMatches == false)
        {
            state.HardFailure = true;
            state.Errors.Add("SHA-256 mismatch: this file is not the exact version bound by expected_sha256.");
        }

        var text = new UTF8Encoding(false, true).GetString(bytes);
        var sourceDocument = ScoreCodec.Read(text, Path.GetFileName(sourcePath), fallbackBpm: AuditPolicy.DiagnosticBpm);
        state.SourceDeclaredBpm = sourceDocument.NeedsBpmConfirmation ? null : sourceDocument.Bpm;
        var catalogMatch = FindCatalogVariant(catalogDocument.RootElement, state.VariantId);
        state.CatalogMatch = catalogMatch.ToReport();

        int auditBpm;
        string bpmSource;
        if (state.BpmKnown == true)
        {
            if (!sourceDocument.NeedsBpmConfirmation)
            {
                auditBpm = sourceDocument.Bpm;
                bpmSource = sourceDocument.Legacy ? "score-filename" : "score-header";
            }
            else if (catalogMatch.BpmKnown && catalogMatch.Bpm is int catalogBpm)
            {
                auditBpm = catalogBpm;
                bpmSource = "catalog-entry";
            }
            else
            {
                throw new InvalidDataException("bpm_known is true, but BPM is absent from the TXT header/name and no unique known-BPM catalog entry matches variant_id.");
            }
        }
        else
        {
            auditBpm = AuditPolicy.DiagnosticBpm;
            bpmSource = "diagnostic-only-override";
        }
        state.AuditBpm = auditBpm;
        state.BpmSource = bpmSource;
        var document = sourceDocument with
        {
            Bpm = auditBpm,
            NeedsBpmConfirmation = state.BpmKnown == false || sourceDocument.NeedsBpmConfirmation && !catalogMatch.BpmKnown
        };

        ParsedScore parsed;
        try
        {
            parsed = ScoreCodec.Parse(document);
            state.ProductionParse = "passed";
        }
        catch (Exception productionError)
        {
            state.HardFailure = true;
            state.ProductionParse = "rejected";
            state.ProductionParseError = DescribeError(productionError);
            // Keep the production rejection visible while using the production body parser with a permissive
            // diagnostic tempo/gap to report every event and every short-note issue where possible.
            var permissive = document with { Bpm = ScoreCodec.MinimumBpm, GapMs = ScoreCodec.MinimumGapMs };
            try
            {
                var diagnosticParse = ScoreCodec.Parse(permissive);
                parsed = new ParsedScore(document, diagnosticParse.Events);
                state.ParseRecovery = "events-recovered-for-diagnostics-only";
            }
            catch (Exception diagnosticError)
            {
                throw new InvalidDataException(
                    "Production parser rejected the score, and diagnostic event recovery also failed: " + DescribeError(diagnosticError),
                    productionError);
            }
        }

        state.EventRows = ExportEvents(parsed, state.BpmKnown == true ? "known_bpm" : "diagnostic_bpm_100_not_verified");
        state.DurationSeconds = parsed.DurationSeconds;
        state.TotalBeats = parsed.TotalBeats;
        state.DurationFlags = FindDurationFlags(parsed, auditBpm);
        state.PitchFlags = FindPitchFlags(parsed);
        state.GapChecks = await CheckGapsAsync(parsed, state.BpmKnown == true, state.ProductionParse == "passed", state.PitchFlags);
        if (state.GapChecks.Any(check => check.Status == "simulation-failed"))
        {
            state.HardFailure = true;
            state.Errors.Add("One or more virtual playback checks failed; see game_gap_checks.");
        }
        state.AcceptedTimeline = CompareAcceptedTimeline(root, itemElement, state.EventRows, state.BpmKnown == true);
        if (state.AcceptedTimeline is { Status: "mismatch" or "invalid" }) state.HardFailure = true;

        if (state.HashMatches == false)
        {
            state.ImportStatus = "skipped-hash-mismatch";
        }
        else if (state.ProductionParse != "passed")
        {
            state.ImportStatus = "skipped-production-parser-rejection";
        }
        else
        {
            var importedItem = await store.ImportAsync(sourcePath);
            if (!importedItem.Available || !string.Equals(importedItem.Hash, state.ActualSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Isolated library import did not preserve the source file hash.");
            if (importedItem.IsCalibrated)
                throw new InvalidDataException("Safety invariant failed: an unreviewed manifest source became calibrated.");
            if (!string.Equals(importedItem.SourceId, null, StringComparison.Ordinal))
                throw new InvalidDataException("Safety invariant failed: isolated empty-catalog import unexpectedly linked to a catalog source.");
            state.ImportStatus = "passed-unreviewed";
            imported.Add(new PendingImport(state, importedItem.Id, state.ActualSha256!));
        }

        if (state.AcceptedTimeline is { Status: "mismatch" or "invalid" })
            state.Errors.Add("Accepted timeline did not validate; see accepted_timeline details.");
    }
    catch (Exception ex)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException ||
            state.SourcePath is null && ex.Message.Contains("path is missing", StringComparison.OrdinalIgnoreCase))
            state.BlockedStatus = "missing-input";
        else if (state.SourcePath is not null && !string.Equals(Path.GetExtension(state.SourcePath), ".txt", StringComparison.OrdinalIgnoreCase))
            state.BlockedStatus = "unsupported-input";
        else
            state.HardFailure = true;
        state.Errors.Add(DescribeError(ex));
    }
    state.RefreshStatus();
    Console.WriteLine($"PROCESSED {state.VariantId ?? "item-" + itemNumber}: {state.EventRows.Length} events; final status is written after restart verification.");
}

string? restartError = null;
try
{
    var restarted = new LibraryStore(profile, assets, () => throw new InvalidOperationException("Network access is forbidden in this audit."));
    await restarted.InitializeAsync();
    foreach (var pending in imported)
    {
        try
        {
            var item = restarted.Items.Single(candidate => candidate.Id == pending.ItemId && candidate.Available);
            if (!string.Equals(item.Hash, pending.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Hash changed after isolated profile restart.");
            if (item.IsCalibrated) throw new InvalidDataException("Unreviewed score became calibrated after restart.");
            pending.State.RestartStatus = "passed-hash-stable-unreviewed";
        }
        catch (Exception ex)
        {
            pending.State.RestartStatus = "failed";
            pending.State.Errors.Add("Restart validation: " + DescribeError(ex));
            pending.State.HardFailure = true;
        }
        pending.State.RefreshStatus();
    }
}
catch (Exception ex)
{
    restartError = DescribeError(ex);
    foreach (var pending in imported)
    {
        pending.State.RestartStatus = "failed";
        pending.State.Errors.Add("Restart validation: " + restartError);
        pending.State.HardFailure = true;
        pending.State.RefreshStatus();
    }
}

foreach (var state in results)
{
    var detailPath = Path.Combine(output, state.DetailFile.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(detailPath)!);
    File.WriteAllText(detailPath, JsonSerializer.Serialize(state.ToDetail(), jsonOptions), new UTF8Encoding(false));
}

var failedCount = results.Count(item => item.Status == "failed");
var issueCount = results.Count(item => item.Status == "issues-found");
var unknownBpmCount = results.Count(item => item.Status == "diagnostic-only-unknown-bpm");
var missingCount = results.Count(item => item.Status == "missing-input");
var unsupportedCount = results.Count(item => item.Status == "unsupported-input");
var report = new
{
    schema_version = 1,
    created_at_utc = DateTimeOffset.UtcNow,
    manifest_path = Path.GetRelativePath(root, manifestPath),
    manifest_sha256 = manifestHash,
    manifest_snapshot = "manifest.json",
    catalog_path = "catalog/songs.json",
    catalog_sha256 = catalogHash,
    item_count = results.Count,
    technical_checks_passed_count = results.Count(item => item.Status == "technical-checks-passed"),
    issues_found_count = issueCount,
    diagnostic_only_unknown_bpm_count = unknownBpmCount,
    missing_input_count = missingCount,
    unsupported_input_count = unsupportedCount,
    failed_count = failedCount,
    isolated_profile = Path.GetRelativePath(output, profile),
    isolated_import_restart = restartError is null ? "completed-per-item" : "failed",
    isolated_restart_error = restartError,
    calibration_invariant = "All manifest imports use an empty isolated catalog and must remain IsCalibrated=false.",
    event_export = "Per-item JSON files include every parsed event with MIDI, beats, start_seconds, duration_seconds and end_seconds.",
    unknown_bpm_policy = "Unknown BPM uses diagnostic_bpm=100 only; no playback pass is claimed.",
    no_live_output = true,
    human_listening = "pending",
    real_game = "not-tested",
    items = results.Select(item => item.ToSummary()).ToArray()
};
File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
Console.WriteLine($"Audited {results.Count} manifest items: {failedCount} failed, {issueCount} with findings, {unknownBpmCount} unknown-BPM diagnostic only, {unsupportedCount} unsupported, {missingCount} missing. Output: {output}");
return failedCount == 0 ? 0 : 1;

static string RequiredString(JsonElement item, string name)
{
    if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        throw new InvalidDataException($"Required string field '{name}' is missing or empty.");
    return value.GetString()!;
}

static string? OptionalString(JsonElement item, string name) =>
    item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static bool RequiredBoolean(JsonElement item, string name)
{
    if (!item.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        throw new InvalidDataException($"Required boolean field '{name}' is missing or invalid.");
    return value.GetBoolean();
}

static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static string DescribeError(Exception error) => error.GetType().Name + ": " + error.Message;

static string ResolveInputPath(string root, string pathText, string description)
{
    if (string.IsNullOrWhiteSpace(pathText)) throw new InvalidDataException($"{description} path is empty.");
    var isAbsolute = Path.IsPathRooted(pathText);
    var fullPath = Path.GetFullPath(isAbsolute ? pathText : Path.Combine(root, pathText));
    if (!isAbsolute && !IsWithin(root, fullPath))
        throw new InvalidDataException($"Relative {description} paths must stay inside the repository.");
    RejectPersonalProfilePath(fullPath);
    RejectReparseChain(fullPath);
    return fullPath;
}

static void ValidateNewOutput(string root, string localRoot, string output)
{
    if (!IsWithin(localRoot, output) || string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(localRoot)), Path.TrimEndingDirectorySeparator(output), StringComparison.OrdinalIgnoreCase))
        throw new IOException("Output must be a new directory below the repository local directory.");
    if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output directory must not already exist: " + output);
    RejectPersonalProfilePath(output);
    RejectReparseChain(Path.GetDirectoryName(output)!);
}

static bool IsWithin(string directory, string path)
{
    var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    var relative = Path.GetRelativePath(fullDirectory, fullPath);
    return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
}

static void RejectPersonalProfilePath(string path)
{
    var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var roamingData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    foreach (var basePath in new[] { localData, roamingData }.Where(value => !string.IsNullOrWhiteSpace(value)))
    {
        var personalLibrary = Path.Combine(basePath, "HarmonicaDesk");
        if (IsWithin(personalLibrary, path))
            throw new InvalidDataException("Reading or writing the real HarmonicaDesk personal library is forbidden.");
    }
}

static void RejectReparseChain(string path)
{
    var current = Path.GetFullPath(path);
    while (!string.IsNullOrEmpty(current))
    {
        if (File.Exists(current) || Directory.Exists(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse-point paths are not accepted by this isolated audit.");
        }
        var parent = Directory.GetParent(current);
        if (parent is null || string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase)) break;
        current = parent.FullName;
    }
}

static CatalogVariant FindCatalogVariant(JsonElement catalog, string variantId)
{
    if (catalog.ValueKind != JsonValueKind.Object || !catalog.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array)
        return CatalogVariant.Missing(variantId);
    var matches = songs.EnumerateArray().Where(entry =>
    {
        var candidate = OptionalString(entry, "variant_id") ?? OptionalString(entry, "id");
        return string.Equals(candidate, variantId, StringComparison.Ordinal);
    }).ToArray();
    if (matches.Length == 0) return CatalogVariant.Missing(variantId);
    if (matches.Length > 1) return CatalogVariant.Ambiguous(variantId, matches.Length);
    var row = matches[0];
    var bpmValue = 0;
    var bpmKnown = row.TryGetProperty("bpm", out var bpmElement) && bpmElement.ValueKind == JsonValueKind.Number && bpmElement.TryGetInt32(out bpmValue);
    int? bpm = bpmKnown ? bpmValue : null;
    return new CatalogVariant(
        "matched",
        OptionalString(row, "id"),
        OptionalString(row, "title"),
        bpm,
        bpmKnown);
}

static EventAudit[] ExportEvents(ParsedScore score, string timingBasis)
{
    var rows = new EventAudit[score.Events.Count];
    var elapsed = 0d;
    int[] semitones = [0, 2, 4, 5, 7, 9, 11];
    for (var index = 0; index < score.Events.Count; index++)
    {
        var note = score.Events[index];
        var duration = note.Beats * 60d / score.Document.Bpm;
        var midi = note.Degree == 0 ? (int?)null : 60 + 12 * note.Octave + semitones[note.Degree - 1] + (note.Sharp ? 1 : 0);
        var start = elapsed;
        elapsed += duration;
        rows[index] = new EventAudit(index + 1, midi, note.Label, note.Beats, start, duration, elapsed, timingBasis);
    }
    return rows;
}

static object[] FindDurationFlags(ParsedScore score, int bpm)
{
    var flags = new List<object>();
    for (var index = 0; index < score.Events.Count; index++)
    {
        var note = score.Events[index];
        var reasons = new List<string>();
        if (!double.IsFinite(note.Beats) || note.Beats <= 0 || note.Beats > ScoreCodec.MaximumBeats)
            reasons.Add("outside_codec_beat_range");
        else
        {
            if (note.Beats < AuditPolicy.VeryShortBeatThreshold) reasons.Add("very_short_less_than_1_over_64_beat");
            if (note.Beats > AuditPolicy.VeryLongBeatThreshold) reasons.Add("very_long_more_than_8_beats");
        }
        var seconds = note.Beats * 60d / bpm;
        if (!double.IsFinite(seconds) || seconds <= 0) reasons.Add("invalid_wall_clock_duration");
        if (reasons.Count > 0)
            flags.Add(new { event_number = index + 1, label = note.Label, beats = note.Beats, duration_seconds = seconds, reasons });
    }
    return flags.ToArray();
}

static object[] FindPitchFlags(ParsedScore score)
{
    var flags = new List<object>();
    int[] semitones = [0, 2, 4, 5, 7, 9, 11];
    for (var index = 0; index < score.Events.Count; index++)
    {
        var note = score.Events[index];
        if (note.Degree == 0) continue;
        var midi = 60 + 12 * note.Octave + semitones[note.Degree - 1] + (note.Sharp ? 1 : 0);
        if (midi is < 48 or > 84)
            flags.Add(new { event_number = index + 1, label = note.Label, midi, supported_range = "48-84" });
    }
    return flags.ToArray();
}

static async Task<GapCheckResult[]> CheckGapsAsync(ParsedScore score, bool bpmKnown, bool productionParsePassed, object[] pitchFlags)
{
    var checks = new List<GapCheckResult>();
    var hasPitchFlags = pitchFlags.Length > 0;
    var timingBasis = bpmKnown ? "declared-bpm-software-check-only" : "diagnostic_bpm_100_not_verified";
    foreach (var gap in new[] { 0, 60, 250 })
    {
        var issues = new List<GameNoteIssue>();
        var noteErrors = new List<object>();
        for (var index = 0; index < score.Events.Count; index++)
        {
            var note = score.Events[index];
            if (note.Degree == 0) continue;
            try
            {
                _ = GameKeyMapping.ForNote(note);
            }
            catch (Exception ex)
            {
                noteErrors.Add(new { event_number = index + 1, label = note.Label, error = DescribeError(ex) });
                continue;
            }
            try
            {
                var single = new ParsedScore(score.Document, new[] { note });
                issues.AddRange(PlaybackEngine.InspectGameNotes(single, gap).Select(issue => issue with { EventNumber = index + 1 }));
            }
            catch (Exception ex)
            {
                noteErrors.Add(new { event_number = index + 1, label = note.Label, error = "InspectGameNotes: " + DescribeError(ex) });
            }
        }

        if (!bpmKnown)
        {
            checks.Add(new GapCheckResult(gap, "diagnostic-only-no-playback", timingBasis, issues.ToArray(), noteErrors.ToArray(), false,
                DiagnosticBpm: AuditPolicy.DiagnosticBpm));
            continue;
        }
        if (!productionParsePassed)
        {
            checks.Add(new GapCheckResult(gap, "not-simulated-production-parser-rejected", timingBasis, issues.ToArray(), noteErrors.ToArray(), false));
            continue;
        }
        if (hasPitchFlags || noteErrors.Count > 0)
        {
            checks.Add(new GapCheckResult(gap, "blocked-unsupported-pitch", timingBasis, issues.ToArray(), noteErrors.ToArray(), false));
            continue;
        }
        if (issues.Count > 0)
        {
            var created = false;
            await using (var engine = new PlaybackEngine(() =>
                         {
                             created = true;
                             return new AuditOutput(new AuditClock());
                         }, new AuditClock()))
            {
                try
                {
                    await engine.PlayAsync(score, PlaybackMode.Game, gameGapMs: gap);
                    throw new InvalidOperationException("PlayAsync unexpectedly accepted a score with inspected short-note issues.");
                }
                catch (InvalidOperationException error) when (!created && error.Message.Contains("过短", StringComparison.Ordinal))
                {
                    checks.Add(new GapCheckResult(gap, "blocked-before-output", timingBasis, issues.ToArray(), noteErrors.ToArray(), false));
                }
                catch (Exception ex)
                {
                    checks.Add(new GapCheckResult(gap, "simulation-failed", timingBasis, issues.ToArray(), noteErrors.ToArray(), created, Error: DescribeError(ex)));
                }
            }
            continue;
        }

        try
        {
            var clock = new AuditClock();
            var output = new AuditOutput(clock);
            await using var engine = new PlaybackEngine(() => output, clock);
            await engine.PlayAsync(score, PlaybackMode.Game, gameGapMs: gap);
            if (output.Notes != score.Events.Count(note => note.Degree != 0) || !output.Released)
                throw new InvalidOperationException("Virtual playback lost notes or failed output cleanup.");
            checks.Add(new GapCheckResult(gap, "software-virtual-playback-passed", timingBasis, [], noteErrors.ToArray(), true,
                NoteCount: output.Notes, ElapsedSeconds: clock.Seconds, MinimumHoldMs: double.IsFinite(output.MinimumHold) ? output.MinimumHold * 1000 : 0));
        }
        catch (Exception ex)
        {
            checks.Add(new GapCheckResult(gap, "simulation-failed", timingBasis, [], noteErrors.ToArray(), true, Error: DescribeError(ex)));
        }
    }
    return checks.ToArray();
}

static TimelineComparison? CompareAcceptedTimeline(string root, JsonElement item, EventAudit[] actual, bool bpmKnown)
{
    if (!item.TryGetProperty("accepted_timeline_path", out var timelineValue) || timelineValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
        timelineValue.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(timelineValue.GetString()))
        return null;
    try
    {
        if (timelineValue.ValueKind != JsonValueKind.String) throw new InvalidDataException("accepted_timeline_path must be a string or null.");
        var path = ResolveInputPath(root, timelineValue.GetString()!, "accepted timeline");
        var bytes = File.ReadAllBytes(path);
        using var expectedDocument = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes));
        var events = expectedDocument.RootElement.ValueKind == JsonValueKind.Array
            ? expectedDocument.RootElement
            : expectedDocument.RootElement.TryGetProperty("events", out var property) && property.ValueKind == JsonValueKind.Array
                ? property
                : throw new InvalidDataException("Accepted timeline JSON must be an array or an object with an events array.");
        var expected = events.EnumerateArray().ToArray();
        var mismatches = new List<object>();
        if (expected.Length != actual.Length) mismatches.Add(new { field = "event_count", expected = expected.Length, actual = actual.Length });
        for (var index = 0; index < Math.Min(expected.Length, actual.Length); index++)
        {
            var row = expected[index];
            if (row.ValueKind != JsonValueKind.Object) { mismatches.Add(new { event_number = index + 1, field = "event", error = "Expected object." }); continue; }
            if (row.TryGetProperty("midi", out var expectedMidi))
            {
                int? value = expectedMidi.ValueKind == JsonValueKind.Null ? null : expectedMidi.TryGetInt32(out var parsedMidi) ? parsedMidi : int.MinValue;
                if (value != actual[index].Midi) mismatches.Add(new { event_number = index + 1, field = "midi", expected = value, actual = actual[index].Midi });
            }
            if (row.TryGetProperty("beats", out var expectedBeats) && expectedBeats.TryGetDouble(out var beats) && Math.Abs(beats - actual[index].Beats) > 0.000001)
                mismatches.Add(new { event_number = index + 1, field = "beats", expected = beats, actual = actual[index].Beats });
            if (bpmKnown)
            {
                CompareTime(row, "start_seconds", actual[index].StartSeconds, index, mismatches);
                CompareTime(row, "end_seconds", actual[index].EndSeconds, index, mismatches);
            }
        }
        var status = mismatches.Count == 0 ? (bpmKnown ? "matched" : "partial-diagnostic-match") : "mismatch";
        return new TimelineComparison(status, Hash(bytes), Path.GetRelativePath(root, path), mismatches.ToArray(), bpmKnown ? null : "start/end comparisons skipped because BPM is unknown.");
    }
    catch (Exception ex)
    {
        return new TimelineComparison("invalid", null, OptionalString(item, "accepted_timeline_path"), [new { error = DescribeError(ex) }], null);
    }
}

static void CompareTime(JsonElement row, string field, double actual, int index, List<object> mismatches)
{
    if (row.TryGetProperty(field, out var value) && value.TryGetDouble(out var expected) && Math.Abs(expected - actual) > 0.001)
        mismatches.Add(new { event_number = index + 1, field, expected, actual });
}

sealed record CatalogVariant(string Status, string? Id, string? Title, int? Bpm, bool BpmKnown, int MatchCount = 0)
{
    public static CatalogVariant Missing(string variantId) => new("not-found", null, null, null, false);
    public static CatalogVariant Ambiguous(string variantId, int count) => new("ambiguous", null, null, null, false, count);
    public object ToReport() => new { status = Status, id = Id, title = Title, bpm = Bpm, bpm_known = BpmKnown, match_count = MatchCount };
}

sealed record EventAudit(int EventNumber, int? Midi, string Label, double Beats, double StartSeconds,
    double DurationSeconds, double EndSeconds, string TimingBasis);

sealed record TimelineComparison(string Status, string? Sha256, string? Path, object[] Mismatches, string? Note);

sealed record GapCheckResult(int GapMs, string Status, string TimingBasis, GameNoteIssue[] Issues,
    object[] NoteErrors, bool OutputCreated, int? NoteCount = null, double? ElapsedSeconds = null,
    double? MinimumHoldMs = null, string? Error = null, int? DiagnosticBpm = null);

sealed record PendingImport(ItemState State, string ItemId, string Hash);

sealed class ItemState(int itemNumber, JsonElement manifestItem)
{
    public int ItemNumber { get; } = itemNumber;
    public JsonElement ManifestItem { get; } = manifestItem;
    public string DetailFile => $"items/{ItemNumber:D4}-{StableSuffix(VariantId ?? "invalid")}.json";
    public string? VariantId { get; set; }
    public string? Title { get; set; }
    public string? SourcePath { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? ActualSha256 { get; set; }
    public bool? HashMatches { get; set; }
    public bool? BpmKnown { get; set; }
    public int? SourceDeclaredBpm { get; set; }
    public int? AuditBpm { get; set; }
    public string? BpmSource { get; set; }
    public object? CatalogMatch { get; set; }
    public string? ProductionParse { get; set; }
    public string? ProductionParseError { get; set; }
    public string? ParseRecovery { get; set; }
    public double? TotalBeats { get; set; }
    public double? DurationSeconds { get; set; }
    public EventAudit[] EventRows { get; set; } = [];
    public object[] DurationFlags { get; set; } = [];
    public object[] PitchFlags { get; set; } = [];
    public GapCheckResult[] GapChecks { get; set; } = [];
    public TimelineComparison? AcceptedTimeline { get; set; }
    public string ImportStatus { get; set; } = "not-attempted";
    public string RestartStatus { get; set; } = "not-imported";
    public string Status { get; private set; } = "failed";
    public string? BlockedStatus { get; set; }
    public bool HardFailure { get; set; }
    public JsonElement? Reference { get; set; }
    public List<string> Errors { get; } = [];

    public void RefreshStatus()
    {
        if (BlockedStatus is not null)
        {
            Status = BlockedStatus;
            return;
        }
        if (HardFailure || Errors.Count > 0 && ProductionParse is null)
        {
            Status = "failed";
            return;
        }
        if (BpmKnown == false)
        {
            Status = "diagnostic-only-unknown-bpm";
            return;
        }
        if (DurationFlags.Length > 0 || PitchFlags.Length > 0 || GapChecks.Any(check =>
                check.Status is "blocked-before-output" or "blocked-unsupported-pitch" or "simulation-failed") ||
            AcceptedTimeline is { Status: "mismatch" })
        {
            Status = "issues-found";
            return;
        }
        Status = RestartStatus == "passed-hash-stable-unreviewed" ? "technical-checks-passed" : "failed";
    }

    public object ToDetail() => new
    {
        schema_version = 1,
        item_number = ItemNumber,
        manifest_item = ManifestItem,
        variant_id = VariantId,
        title = Title,
        source_path = SourcePath,
        expected_sha256 = ExpectedSha256,
        actual_sha256 = ActualSha256,
        hash_matches = HashMatches,
        bpm_known = BpmKnown,
        source_declared_bpm = SourceDeclaredBpm,
        audit_bpm = AuditBpm,
        bpm_source = BpmSource,
        diagnostic_bpm = BpmKnown == false ? (int?)AuditPolicy.DiagnosticBpm : null,
        accepted_timeline_scope = "Comparison is relative to the supplied accepted timeline JSON and does not establish accuracy against an audio recording.",
        catalog_match = CatalogMatch,
        production_parse = ProductionParse,
        production_parse_error = ProductionParseError,
        parse_recovery = ParseRecovery,
        total_beats = TotalBeats,
        duration_seconds = DurationSeconds,
        duration_review_thresholds = new { very_short_less_than_beats = AuditPolicy.VeryShortBeatThreshold, very_long_more_than_beats = AuditPolicy.VeryLongBeatThreshold },
        duration_flags = DurationFlags,
        pitch_flags = PitchFlags,
        game_gap_checks = GapChecks,
        accepted_timeline = AcceptedTimeline,
        imported_isolated = ImportStatus,
        restart_hash_validation = RestartStatus,
        is_calibrated = false,
        status = Status,
        errors = Errors,
        human_listening = "pending",
        real_game = "not-tested",
        events = EventRows
    };

    public object ToSummary() => new
    {
        item_number = ItemNumber,
        variant_id = VariantId,
        title = Title,
        format = ManifestItem.TryGetProperty("format", out var formatValue) && formatValue.ValueKind == JsonValueKind.String ? formatValue.GetString() : null,
        source_status = ManifestItem.TryGetProperty("status", out var sourceStatusValue) && sourceStatusValue.ValueKind == JsonValueKind.String ? sourceStatusValue.GetString() : null,
        status = Status,
        bpm_known = BpmKnown,
        audit_bpm = AuditBpm,
        diagnostic_bpm = BpmKnown == false ? (int?)AuditPolicy.DiagnosticBpm : null,
        accepted_timeline_status = AcceptedTimeline?.Status,
        event_count = EventRows.Length,
        total_beats = TotalBeats,
        duration_seconds = DurationSeconds,
        source_sha256 = ActualSha256,
        hash_matches = HashMatches,
        imported_isolated = ImportStatus,
        restart_hash_validation = RestartStatus,
        detail_file = DetailFile,
        human_listening = "pending",
        real_game = "not-tested",
        errors = Errors
    };

    private static string StableSuffix(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];
}

sealed class AuditClock : IPlaybackClock
{
    public double Seconds { get; private set; }
    public void Advance(double seconds) => Seconds += seconds;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Seconds += Math.Max(0.0000001, delay.TotalSeconds);
        return Task.CompletedTask;
    }
}

sealed class AuditOutput(AuditClock clock) : IPlaybackOutput
{
    public int Notes { get; private set; }
    public bool Released { get; private set; }
    public double MinimumHold { get; private set; } = double.MaxValue;
    private double? noteOn;
    public Task BeginAsync(PlaybackMode mode, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task NoteOnAsync(ScoreEvent note, float volume, CancellationToken cancellationToken)
    {
        if (GameKeyMapping.ForNote(note).Any(key => key.Mouse)) clock.Advance(0.024);
        noteOn = clock.Seconds;
        Notes++;
        return Task.CompletedTask;
    }
    public Task SilenceAsync(CancellationToken cancellationToken)
    {
        if (noteOn is double started) MinimumHold = Math.Min(MinimumHold, clock.Seconds - started);
        noteOn = null;
        return Task.CompletedTask;
    }
    public void CheckSafety() { }
    public void SetVolume(float volume) { }
    public Task ReleaseAsync(CancellationToken cancellationToken) { Released = true; return Task.CompletedTask; }
    public void Dispose() { }
}

static class AuditPolicy
{
    public const int DiagnosticBpm = 100;
    public const int MaximumScoreBytes = 1_048_576;
    public const double VeryShortBeatThreshold = 1d / 64;
    public const double VeryLongBeatThreshold = 8;
}
