using System.Text;
using System.Text.Json;
using Harmonica.Core;
using Harmonica.Desktop.Services;

static class BundledLibraryChecks
{
    private static byte[] Score(string body = "1 2_ 3_ 0 |", bool bom = false)
    {
        var text = ScoreCodec.Write(new ScoreDocument("内置测试曲", 90, 20, body));
        if (bom) text = "\uFEFF" + text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return Encoding.UTF8.GetBytes(text);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static System.Net.Http.HttpClient NoNetwork() => throw new InvalidOperationException("内置曲库不能创建网络客户端。");

    private static async Task Bundle(Fixture fixture, byte[] bytes, string id = "bundled-test", string fileName = "bundled.txt")
    {
        var hash = LibraryStore.Hash(bytes);
        var record = new
        {
            id, title = "内置测试曲", filename = "内置测试曲 bpm90.txt", bpm = 90,
            work_id = "work-" + id, variant_id = id, is_default_variant = true, acquisition = "external-import",
            source_url = "https://example.com/source", variant = "测试片段", verified_sha256 = hash,
            source_sha256 = new string('a', 64), verification = new { rhythm = "reviewed" },
            review_note = "测试逐音审阅记录。", review_method = "测试音高、时值与解析器比较。",
            reference = new
            {
                url = "https://example.com/reference-recording", edition = "测试录音版本",
                audio_sha256 = new string('b', 64), start_seconds = 0.0
            },
            evidence = new[] { new { file = $"catalog/calibration-evidence/{id}.json", sha256 = new string('c', 64) } },
            validation = new { production_parser = "passed", reviewed_note_count = 10, maximum_parser_boundary_error_seconds = .000001 }
        };
        await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { schema_version = 3, songs = new[] { record } }));
        var directory = Path.Combine(fixture.Assets, "BundledScores");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
        await Manifest(fixture, new[] { new { catalog_id = id, file = fileName, sha256 = hash } });
    }

    private static Task Manifest(Fixture fixture, object entries, int schema = 1) =>
        File.WriteAllTextAsync(Path.Combine(fixture.Assets, "BundledScores", "manifest.json"),
            JsonSerializer.Serialize(new { schema_version = schema, songs = entries }));

    public static async Task FirstOffline()
    {
        using var fixture = new Fixture();
        var original = Score(bom: true);
        await Bundle(fixture, original);
        var store = await fixture.CreateStore(NoNetwork);
        var item = store.Items.Single(item => item.SourceId == "bundled-test");
        Check(item.Available && item.IsCalibrated && store.Warnings.Count == 0, "首次离线启动须提供经过校验的可用内置曲谱。");
        Check(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(store.ScorePath(item))), "内置谱 BOM 和换行原始字节不得转换。");
        Check((await store.ReadDocumentAsync(item)).Body.Contains("1 2_ 3_"), "内置曲谱必须能由正式解析器读取。");
        Check(store.GetWorks().Single(work => work.WorkId == "work-bundled-test").SelectedVariant.Id == item.Id, "作品应默认选择可用内置版本。");
    }

    public static async Task RestartAndPreferences()
    {
        using var fixture = new Fixture();
        await Bundle(fixture, Score());
        var store = await fixture.CreateStore(NoNetwork);
        var item = store.Items.Single(item => item.SourceId == "bundled-test");
        item.Favorite = true;
        item.LastPlayed = DateTimeOffset.Parse("2026-10-14T12:34:56Z");
        store.Settings.SelectedVariants[item.WorkId] = item.Id;
        store.Settings.LastSelectedId = item.Id;
        await store.SaveLibraryAsync();
        await store.SaveSettingsAsync();
        var files = Directory.GetFiles(store.ScoresDirectory).ToDictionary(path => path, File.GetLastWriteTimeUtc);
        var reopened = await fixture.CreateStore(NoNetwork);
        var restored = reopened.Items.Single(entry => entry.SourceId == "bundled-test");
        Check(restored.Id == item.Id && restored.FileName == item.FileName && restored.Favorite && restored.LastPlayed == item.LastPlayed,
            "重启须复用同来源同哈希文件并保留收藏、最近播放。");
        Check(reopened.Settings.SelectedVariants[item.WorkId] == item.Id && reopened.Settings.LastSelectedId == item.Id,
            "重启须保留选中的曲谱和版本。");
        Check(files.Count == Directory.GetFiles(store.ScoresDirectory).Length && files.All(pair => File.GetLastWriteTimeUtc(pair.Key) == pair.Value),
            "重启不得复制重复曲谱或重写现有曲谱。");
    }

    public static async Task EditedVersion()
    {
        using var fixture = new Fixture();
        var original = Score();
        await Bundle(fixture, original);
        var store = await fixture.CreateStore(NoNetwork);
        var edited = store.Items.Single(item => item.SourceId == "bundled-test");
        await store.SaveDocumentAsync(edited, new ScoreDocument("我的修改", 110, 30, "7:2 0 |"));
        edited.Favorite = true;
        edited.LastPlayed = DateTimeOffset.Parse("2026-10-14T12:34:56Z");
        store.Settings.SelectedVariants[edited.WorkId] = edited.Id;
        await store.SaveLibraryAsync();
        await store.SaveSettingsAsync();
        var editedBytes = await File.ReadAllBytesAsync(store.ScorePath(edited));
        var reopened = await fixture.CreateStore(NoNetwork);
        var versions = reopened.Items.Where(item => item.SourceId == "bundled-test").ToArray();
        Check(versions.Length == 2, "已有编辑内容时须单独补回内置原版。");
        var preserved = versions.Single(item => item.Id == edited.Id);
        var bundled = versions.Single(item => item.Id != edited.Id);
        Check(preserved.Title == "我的修改" && preserved.Bpm == 110 && preserved.Favorite && preserved.LastPlayed == edited.LastPlayed && !preserved.IsCalibrated,
            "编辑版本身份、速度、收藏与最近播放不得改变。");
        Check(Enumerable.SequenceEqual(editedBytes, await File.ReadAllBytesAsync(reopened.ScorePath(preserved))), "编辑后的文件字节不得覆盖。");
        Check(bundled.Available && bundled.IsCalibrated && bundled.Variant.Contains("内置原版") &&
            Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(reopened.ScorePath(bundled))), "原版须独立存储并明确标识。");
        Check(reopened.GetWorks().Single(work => work.WorkId == edited.WorkId).SelectedVariant.Id == edited.Id, "不能覆盖用户已保存的版本选择。");
        var again = await fixture.CreateStore(NoNetwork);
        Check(again.Items.Count(item => item.SourceId == "bundled-test") == 2, "再次启动不能继续增加内置副本。");
    }

    public static async Task UpgradeAndOrphans()
    {
        using var fixture = new Fixture();
        await Bundle(fixture, Score());
        var store = await fixture.CreateStore(NoNetwork);
        var old = store.Items.Single(item => item.SourceId == "bundled-test");
        var oldBytes = await File.ReadAllBytesAsync(store.ScorePath(old));
        var replacement = Score("4 5 6 7 |", bom: true);
        await Bundle(fixture, replacement);
        var hash = LibraryStore.Hash(replacement);
        var target = Path.Combine(store.ScoresDirectory, "bundled-" + LibraryStore.Hash(Encoding.UTF8.GetBytes("bundled-test"))[..16] + "-" + hash[..16] + ".txt");
        var orphan = Score("7 6 5 4 |", bom: true);
        await File.WriteAllBytesAsync(target, orphan);
        var upgraded = await fixture.CreateStore(NoNetwork);
        Check(Enumerable.SequenceEqual(oldBytes, await File.ReadAllBytesAsync(store.ScorePath(old))) && Enumerable.SequenceEqual(orphan, await File.ReadAllBytesAsync(target)),
            "曲库更新须保留旧版及索引外同名文件。");
        var added = upgraded.Items.Single(item => item.SourceId == "bundled-test" && item.IsCalibrated);
        Check(added.Id != old.Id && upgraded.ScorePath(added) != target && Enumerable.SequenceEqual(replacement, await File.ReadAllBytesAsync(upgraded.ScorePath(added))),
            "升级内置版本须避开冲突文件并提供新谱。");
        var count = Directory.GetFiles(store.ScoresDirectory).Length;
        var reopened = await fixture.CreateStore(NoNetwork);
        Check(reopened.Items.Count(item => item.SourceId == "bundled-test") == 2 && Directory.GetFiles(store.ScoresDirectory).Length == count,
            "升级后重启不能产生重复版本。");
        // An interrupted first run may leave a verified file before library.json is saved.
        using var recoveredFixture = new Fixture();
        await Bundle(recoveredFixture, replacement);
        var recoveredRoot = Path.Combine(recoveredFixture.Root, "个人 数据", "Scores");
        Directory.CreateDirectory(recoveredRoot);
        var recoveredPath = Path.Combine(recoveredRoot, Path.GetFileName(target));
        await File.WriteAllBytesAsync(recoveredPath, replacement);
        var recovered = await recoveredFixture.CreateStore(NoNetwork);
        Check(recovered.ScorePath(recovered.Items.Single(item => item.SourceId == "bundled-test")) == recoveredPath,
            "恢复中断的初始化时可复用哈希匹配的孤儿文件。");
    }

    public static async Task InvalidBundles()
    {
        foreach (var problem in new[] { "bytes", "hash-binding", "path", "backslash-path", "unknown-id", "duplicate", "schema", "json", "missing-file", "invalid-score", "empty", "omitted", "missing-manifest", "total-scores", "total-works", "total-bytes" })
        {
            using var fixture = new Fixture();
            var bytes = Score();
            await Bundle(fixture, bytes);
            var directory = Path.Combine(fixture.Assets, "BundledScores");
            var entry = new { catalog_id = "bundled-test", file = "bundled.txt", sha256 = LibraryStore.Hash(bytes) };
            switch (problem)
            {
                case "bytes": await File.WriteAllBytesAsync(Path.Combine(directory, entry.file), Score("7 7 |")); break;
                case "hash-binding": await Manifest(fixture, new[] { entry with { sha256 = new string('f', 64) } }); break;
                case "path": await Manifest(fixture, new[] { entry with { file = "../bundled.txt" } }); break;
                case "backslash-path": await Manifest(fixture, new[] { entry with { file = "..\\bundled.txt" } }); break;
                case "unknown-id": await Manifest(fixture, new[] { entry with { catalog_id = "unknown" } }); break;
                case "duplicate": await Manifest(fixture, new[] { entry, entry }); break;
                case "schema": await Manifest(fixture, new[] { entry }, 2); break;
                case "json": await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), "{"); break;
                case "missing-file": File.Delete(Path.Combine(directory, entry.file)); break;
                case "invalid-score": await Bundle(fixture, Encoding.UTF8.GetBytes("This is not a score")); break;
                case "empty": await Manifest(fixture, Array.Empty<object>()); break;
                case "omitted":
                    var catalogPath = Path.Combine(fixture.Assets, "catalog.json");
                    using (var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath)))
                    {
                        var original = catalog.RootElement.GetProperty("songs")[0].Clone();
                        var omitted = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(original.GetRawText())!;
                        omitted["id"] = JsonSerializer.SerializeToElement("omitted-source");
                        omitted["work_id"] = JsonSerializer.SerializeToElement("omitted-work");
                        await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(new { schema_version = 3, songs = new object[] { original, omitted } }));
                    }
                    break;
                case "missing-manifest": File.Delete(Path.Combine(directory, "manifest.json")); break;
                case "total-scores":
                case "total-works":
                case "total-bytes":
                    await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["schema_version"] = 1, ["songs"] = new[] { entry }, [problem.Replace('-', '_')] = 0
                    }));
                    break;
            }
            var store = await fixture.CreateStore(NoNetwork);
            Check(store.Items.Single(item => item.SourceId == "bundled-test").Available == false && store.Warnings.Any(w => w.Contains("内置曲库未加载")),
                problem + "：损坏包必须诊断并保持该来源不可用。");
            Check(Directory.GetFiles(store.ScoresDirectory).Length == 1, problem + "：完整包校验前不得写入任何内置曲谱。");
        }
    }
}
