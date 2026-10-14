using System.Diagnostics;
using System.Text.Json;
using Harmonica.Core;
using Harmonica.Desktop.Models;
using Harmonica.Desktop.Services;

internal static class RealBundledLibraryAudit
{
    private const string ProfileMarker = ".harmonica-bundle-audit-profile";
    private sealed record FileState(string Sha256, long Bytes, DateTime ModifiedUtc);
    private sealed record PersonalState(string Id, bool Favorite, DateTimeOffset? LastPlayed);

    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 4 || args[0] != "--audit-bundle")
            throw new ArgumentException("--audit-bundle ASSETS ISOLATED_PROFILE REPORT; nonempty copied profiles require " + ProfileMarker);
        var assets = Path.GetFullPath(args[1]);
        var profile = Path.GetFullPath(args[2]);
        var report = Path.GetFullPath(args[3]);
        var realProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HarmonicaDesk");
        if (ContainsPath(profile, realProfile) || ContainsPath(realProfile, profile) ||
            ContainsPath(realProfile, report) || ContainsPath(assets, profile) || ContainsPath(profile, assets) ||
            ContainsPath(assets, report) || ContainsPath(profile, report))
            throw new ArgumentException("Audit requires an isolated profile and a report outside both profile and assets; the real HarmonicaDesk profile is forbidden.");
        if (!Directory.Exists(assets)) throw new DirectoryNotFoundException(assets);
        RejectLinks(profile);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(report)!); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Audit reports cannot be inside a link or junction.");
        if (File.Exists(report) && (File.GetAttributes(report) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Audit reports cannot overwrite a link.");
        var fresh = !Directory.Exists(profile) || !Directory.EnumerateFileSystemEntries(profile).Any(path => Path.GetFileName(path) != ProfileMarker);
        if (!fresh && !File.Exists(Path.Combine(profile, ProfileMarker)))
            throw new ArgumentException("Existing profile copies must be explicitly marked with " + ProfileMarker + ". Never point this audit at personal data.");

        var watch = Stopwatch.StartNew();
        var checks = new List<object>();
        var failures = new List<string>();
        var warnings = new List<string>();
        var versions = 0;
        var works = 0;
        var availableVersions = 0;
        var parsedVersions = 0;
        var availableWorks = 0;
        var defaultsAvailable = 0;
        var firstScoreCount = 0;
        var secondScoreCount = 0;
        long firstInitializeMs = 0;
        long secondInitializeMs = 0;
        var networkAttempts = 0;
        var scoreRoot = Path.Combine(profile, "Scores");
        var beforeFiles = SnapshotFiles(scoreRoot);
        var libraryPath = Path.Combine(profile, "library.json");
        var settingsPath = Path.Combine(profile, "settings.json");
        var beforeSettings = File.Exists(settingsPath) ? await File.ReadAllBytesAsync(settingsPath) : null;
        var preferences = File.Exists(libraryPath)
            ? (JsonSerializer.Deserialize<List<LibraryItem>>(await File.ReadAllTextAsync(libraryPath)) ?? [])
                .Select(item => new PersonalState(item.Id, item.Favorite, item.LastPlayed)).ToArray()
            : [];
        var selected = beforeSettings is null ? new Dictionary<string, string>() :
            JsonSerializer.Deserialize<UserSettings>(await File.ReadAllTextAsync(settingsPath))?.SelectedVariants ?? [];
        void Check(string name, bool passed, string detail)
        {
            checks.Add(new { name, passed, detail });
            if (!passed) failures.Add(name + ": " + detail);
        }
        System.Net.Http.HttpClient NoNetwork()
        {
            networkAttempts++;
            throw new InvalidOperationException("The bundled-library audit forbids network access.");
        }

        try
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(assets, "BundledScores", "manifest.json")));
            var entries = manifest.RootElement.GetProperty("songs").EnumerateArray().Select(entry => new
            {
                Id = entry.GetProperty("catalog_id").GetString()!,
                Hash = entry.GetProperty("sha256").GetString()!
            }).ToArray();
            versions = entries.Length;
            Check("manifest-count", versions == 374 && entries.Select(entry => entry.Id).Distinct().Count() == 374,
                $"Expected 374 unique bundled versions; found {versions} entries.");
            Directory.CreateDirectory(profile);
            if (!File.Exists(Path.Combine(profile, ProfileMarker)))
                await File.WriteAllTextAsync(Path.Combine(profile, ProfileMarker), "Isolated test profile for --audit-bundle.\n");
            var store = new LibraryStore(profile, assets, NoNetwork);
            var start = watch.ElapsedMilliseconds;
            await store.InitializeAsync();
            firstInitializeMs = watch.ElapsedMilliseconds - start;
            warnings.AddRange(store.Warnings);
            var bundledWorks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!store.Catalog.TryGetValue(entry.Id, out var source))
                {
                    Check("catalog-binding:" + entry.Id, false, "Bundled source is absent from catalog.");
                    continue;
                }
                bundledWorks.Add(source.WorkId);
                var item = store.Items.FirstOrDefault(item => item.SourceId == entry.Id && item.Available && item.IsCalibrated &&
                    string.Equals(item.Hash, entry.Hash, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                {
                    Check("availability:" + entry.Id, false, "No available calibrated file matches this bundled version.");
                    continue;
                }
                var bytes = await File.ReadAllBytesAsync(store.ScorePath(item));
                if (!string.Equals(LibraryStore.Hash(bytes), entry.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    Check("file-hash:" + entry.Id, false, "Personal score bytes do not match manifest.");
                    continue;
                }
                availableVersions++;
                var document = await store.ReadDocumentAsync(item);
                var parsed = ScoreCodec.Parse(document);
                if (parsed.Events.Count > 0 && parsed.DurationSeconds > 0) parsedVersions++;
                else Check("parser:" + entry.Id, false, "Production parser produced an empty score.");
            }
            works = bundledWorks.Count;
            var libraryWorks = store.GetWorks().Where(work => bundledWorks.Contains(work.WorkId)).ToArray();
            availableWorks = libraryWorks.Count(work => work.AllVariants.Any(item => item.Available));
            defaultsAvailable = libraryWorks.Count(work => work.SelectedVariant.Available);
            Check("bundled-availability", availableVersions == 374 && parsedVersions == 374,
                $"Available matching hashes: {availableVersions}; production-parser successes: {parsedVersions}.");
            Check("work-coverage", works == 244 && availableWorks == 244,
                $"Bundled works: {works}; works with an available version: {availableWorks}.");
            if (fresh) Check("fresh-defaults", defaultsAvailable == 244, $"Fresh-profile selected variants available: {defaultsAvailable}/244.");
            Check("original-score-bytes", Preserved(beforeFiles, SnapshotFiles(scoreRoot), checkTime: false),
                $"All {beforeFiles.Count} original score files must retain their bytes.");
            Check("personal-metadata", PreservedPreferences(preferences, store),
                $"All {preferences.Length} existing item IDs, favorites and recent timestamps must remain.");
            Check("selected-variants", selected.All(pair => store.Settings.SelectedVariants.TryGetValue(pair.Key, out var value) && value == pair.Value),
                $"All {selected.Count} saved version choices must remain.");
            Check("settings-bytes", await SameSettings(settingsPath, beforeSettings), "The settings file must remain byte-for-byte unchanged, including absence on a fresh profile.");
            var afterFirst = SnapshotFiles(scoreRoot);
            firstScoreCount = afterFirst.Count;

            var reopened = new LibraryStore(profile, assets, NoNetwork);
            start = watch.ElapsedMilliseconds;
            await reopened.InitializeAsync();
            secondInitializeMs = watch.ElapsedMilliseconds - start;
            warnings.AddRange(reopened.Warnings);
            var afterSecond = SnapshotFiles(scoreRoot);
            secondScoreCount = afterSecond.Count;
            Check("restart-files", afterFirst.Count == afterSecond.Count && Preserved(afterFirst, afterSecond, checkTime: true),
                $"Restart score counts {afterFirst.Count} -> {afterSecond.Count}; every hash, byte size and modification time must remain.");
            Check("restart-item-count", store.Items.Count == reopened.Items.Count, $"Restart library items {store.Items.Count} -> {reopened.Items.Count}.");
            Check("restart-personal-metadata", PreservedPreferences(preferences, reopened), "Restart must preserve original item identity, favorites and recents.");
            Check("restart-selected-variants", selected.All(pair => reopened.Settings.SelectedVariants.TryGetValue(pair.Key, out var value) && value == pair.Value),
                "Restart must preserve original version choices.");
            Check("restart-settings-bytes", await SameSettings(settingsPath, beforeSettings), "Restart must not rewrite settings.");
            Check("warnings", warnings.Count == 0, string.Join(" | ", warnings));
            Check("offline", networkAttempts == 0, $"Network client attempts: {networkAttempts}.");
        }
        catch (Exception ex)
        {
            Check("audit-exception", false, ex.ToString());
        }
        watch.Stop();
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
        {
            passed = failures.Count == 0,
            assets, profile, fresh_profile = fresh, original_score_files = beforeFiles.Count,
            manifest_versions = versions, catalog_works = works, available_versions = availableVersions,
            parsed_versions = parsedVersions, available_works = availableWorks, selected_available_works = defaultsAvailable,
            first_score_files = firstScoreCount, second_score_files = secondScoreCount,
            first_initialize_ms = firstInitializeMs, second_initialize_ms = secondInitializeMs, elapsed_ms = watch.ElapsedMilliseconds,
            network_attempts = networkAttempts, warnings, checks, failures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Bundle audit: {(failures.Count == 0 ? "PASS" : "FAIL")}; {availableVersions}/374 versions, {availableWorks}/244 works, {watch.ElapsedMilliseconds} ms. Report: {report}");
        foreach (var failure in failures) Console.Error.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static bool ContainsPath(string parent, string child)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        return string.Equals(prefix, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectLinks(string profile)
    {
        for (var directory = new DirectoryInfo(profile); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Audit profiles cannot be inside a link or junction.");
        if (!Directory.Exists(profile)) return;
        var pending = new Stack<string>();
        pending.Push(profile);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Audit profiles cannot contain links or junctions.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    private static Dictionary<string, FileState> SnapshotFiles(string root) => !Directory.Exists(root) ? [] :
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(root, path),
            path => new FileState(LibraryStore.Hash(File.ReadAllBytes(path)), new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)),
            StringComparer.OrdinalIgnoreCase);

    private static bool Preserved(Dictionary<string, FileState> before, Dictionary<string, FileState> after, bool checkTime) =>
        before.All(pair => after.TryGetValue(pair.Key, out var current) && pair.Value.Sha256 == current.Sha256 &&
            pair.Value.Bytes == current.Bytes && (!checkTime || pair.Value.ModifiedUtc == current.ModifiedUtc));

    private static bool PreservedPreferences(PersonalState[] before, LibraryStore store) => before.All(previous =>
        store.Items.Any(item => item.Id == previous.Id && item.Favorite == previous.Favorite && item.LastPlayed == previous.LastPlayed));

    private static async Task<bool> SameSettings(string path, byte[]? before) => before is null ? !File.Exists(path) :
        File.Exists(path) && Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path));
}
