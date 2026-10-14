using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using Harmonica.Core;
using Harmonica.Desktop.Models;
using Harmonica.Desktop.Services;

if (args.Length > 0) return await CatalogAudit.Run(args);

var scenarios = new (string Name, Func<Task> Run)[]
{
    ("首次初始化与升级保留示例", Initialization),
    ("中文路径复制导入、保留原文件与内容去重", ImportCopy),
    ("同名不同内容和保存副本均不覆盖", SameNameAndCopies),
    ("曲谱、收藏和设置重启持久化", Persistence),
    ("旧曲库迁移保留未知字段", LibrarySchemaMigration),
    ("作品分组、多版本选择与同名独立", WorkGroupingAndSelection),
    ("移除来源后隐藏旧占位，保留个人文件、收藏与缺文件记录", RetiredCatalogPreviews),
    ("出版谱参考字段、SHA与界面标签校验", PublishedSheetReferenceMetadata),
    ("质量筛选只显示匹配版本并保留已保存选择", LibraryQualityFiltering),
    ("多词搜索、相关度、规范化、作品视图与版本隔离", LibrarySearchChecks.Run),
    ("曲库宽度与排序持久化，兼容旧设置并校验异常值", LibraryPreferences),
    ("版本说明显示参考片段定位与谱面时长", CatalogVersionDescription),
    ("外部来源未知速度导入实际 BPM 并在重启后保留", UnknownCatalogBpmImport),
    ("schema 2 与直接下载来源仍要求合法 BPM", StrictCatalogBpmValidation),
    ("外部来源加载、校准SHA绑定与编辑撤销", ExternalImportAndCalibration),
    ("暂缓校对来源可导入且不会获得校准状态", DeferredSourceImport),
    ("指定来源导入相同个人文件后保留两项并应用校准", ExplicitSourceImportWithExistingPersonalDuplicate),
    ("同来源本机导入版本标签与默认身份重启保留", CatalogCopyVariantPersistence),
    ("拒绝带userinfo的HTTPS来源、参考和下载链接", HttpsUserInfoRejected),
    ("未知速度确认状态与新格式保存", BpmConfirmation),
    ("无效谱面和超限导入不修改曲库", InvalidImports),
    ("损坏索引恢复保留已编辑示例", DamagedIndexRecovery),
    ("损坏索引不替换无法解析的已有示例", DamagedExampleRecovery),
    ("保存失败保留旧文件并可重试", FailedSave),
    ("非法曲库路径拒绝且保留原索引", InvalidPaths),
    ("旧使用包目录与导入数量上限", OldKitDirectory),
    ("并发JSON写入保留最终状态", ConcurrentMetadata),
    ("下载取消与无效来源不写入", DownloadFailures),
    ("社区来源校验、本地转换、离线重开与升级保留", ConvertedDownloadAndUpgrade),
    ("下载失败重试、校验失败与防覆盖", VerifiedDownloadFailures),
    ("旧设置添加跟谱默认值并保留已有热键", GuideMigration),
    ("跟谱设置重启保留，异常尺寸和倍率恢复安全值", GuidePersistence),
    ("游戏留白兼容旧设置、保存选择并恢复无效值", GameGapSettings),
};
var failures = 0;
foreach (var scenario in scenarios)
{
    try { await scenario.Run(); Console.WriteLine("PASS " + scenario.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + scenario.Name + "\n" + ex); }
}
Console.WriteLine($"Desktop storage checks: {scenarios.Length - failures}/{scenarios.Length} passed.");
return failures == 0 ? 0 : 1;

static ScoreDocument Doc(string body = "1 0 2_ 3_ |", string title = "原创个人谱") => new(title, 100, 25, body);
static string Text(string body = "1 0 2_ 3_ |", string title = "原创个人谱") => ScoreCodec.Write(Doc(body, title));

static async Task LibraryPreferences()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    Check(store.Settings.LibrarySidebarWidth == 330 && store.Settings.LibrarySort == LibrarySortOrder.Smart, "旧设置提供曲库默认值。");
    store.Settings.LibrarySidebarWidth = 390;
    store.Settings.LibrarySort = LibrarySortOrder.Title;
    await store.SaveSettingsAsync();
    var reopened = await fixture.CreateStore();
    Check(reopened.Settings.LibrarySidebarWidth == 390 && reopened.Settings.LibrarySort == LibrarySortOrder.Title, "宽度和排序重启保留。");
    reopened.Settings.LibrarySidebarWidth = 9999;
    reopened.Settings.LibrarySort = (LibrarySortOrder)999;
    await reopened.SaveSettingsAsync();
    var normalized = await fixture.CreateStore();
    Check(normalized.Settings.LibrarySidebarWidth == 420 && normalized.Settings.LibrarySort == LibrarySortOrder.Smart, "无效偏好不能破坏布局。");
}

static async Task Initialization()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    Check(store.Items.Count == 1, "初次安装应只有原创示例。");
    var example = store.Items.Single();
    Check(example.Example && example.Available, "原创示例应可离线读取。");
    await store.SaveDocumentAsync(example, Doc("7:2 0", "修改过的示例"));
    var reopened = await fixture.CreateStore();
    var read = await reopened.ReadDocumentAsync(reopened.Items.Single());
    Check(read.Title == "修改过的示例" && read.Body == "7:2 0", "重复初始化不得重置用户修改。");
}

static async Task GameGapSettings()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    Check(store.Settings.GameNoteGapMs == 60, "旧配置默认使用60ms游戏留白。");
    foreach (var value in new[] { 0, 90, 250, -1, 999 })
    {
        store.Settings.GameNoteGapMs = value;
        await store.SaveSettingsAsync();
        var reopened = await fixture.CreateStore();
        Check(reopened.Settings.GameNoteGapMs == (value is >= 0 and <= 250 ? value : 60), "合法值重启保留，无效值恢复默认。");
    }
}

static async Task ImportCopy()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var path = await fixture.Source("中文 路径/曲子.txt", Text());
    var original = await File.ReadAllBytesAsync(path);
    var item = await store.ImportAsync(path);
    Check(store.ScorePath(item) != path && File.Exists(path), "导入应复制到个人目录并保留原文件。");
    var sourceAfterImport = await File.ReadAllBytesAsync(path);
    Check(original.SequenceEqual(sourceAfterImport), "导入不得修改来源文件。");
    Check((await store.ReadDocumentAsync(item)).Body == Doc().Body, "复制正文应一致。");
    var duplicate = await store.ImportAsync(path);
    Check(ReferenceEquals(item, duplicate) && store.Items.Count == 2, "相同内容应复用已有条目。");
}

static async Task SameNameAndCopies()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var first = await store.ImportAsync(await fixture.Source("甲/同名.txt", Text("1 2")));
    var second = await store.ImportAsync(await fixture.Source("乙/同名.txt", Text("3 4")));
    Check(first.Id != second.Id && first.FileName != second.FileName, "同名不同内容须使用不同文件。");
    var before = await File.ReadAllTextAsync(store.ScorePath(first));
    var copy = await store.SaveCopyAsync((await store.ReadDocumentAsync(first)) with { Body = "7 0" });
    Check(copy.Id != first.Id && before == await File.ReadAllTextAsync(store.ScorePath(first)), "另存副本不得覆盖原谱。");
    Check((await store.ReadDocumentAsync(second)).Body == "3 4", "另一同名曲谱不得改变。");
}

static async Task Persistence()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var item = await store.ImportTextAsync(Text());
    item.Favorite = true;
    item.LastPlayed = DateTimeOffset.Parse("2026-01-02T03:04:05+00:00");
    store.Settings.Theme = "Dark";
    store.Settings.Volume = .35f;
    store.Settings.LastSelectedId = item.Id;
    await store.SaveLibraryAsync();
    await store.SaveSettingsAsync();
    var reopened = await fixture.CreateStore();
    var saved = reopened.Items.Single(i => i.Id == item.Id);
    Check(saved.Favorite && saved.LastPlayed == item.LastPlayed && saved.Available, "曲库状态应重启保留。");
    Check(reopened.Settings.Theme == "Dark" && reopened.Settings.Volume == .35f && reopened.Settings.LastSelectedId == item.Id, "设置应重启保留。");
    Check((await reopened.ReadDocumentAsync(saved)).GapMs == 25, "曲谱间隔应持久化。");
}

static async Task LibrarySchemaMigration()
{
    using var fixture = new Fixture();
    var sourceRecord = new
    {
        id = "legacy-source", title = "旧版来源", filename = "legacy.txt", bpm = 100, size = 1,
        sha256 = LibraryStore.Hash([49]), download_url = "https://raw.githubusercontent.com/example/example/main/legacy.txt",
        source_url = "https://github.com/example/example", future_catalog_value = new { keep = true }
    };
    await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { schema_version = 2, songs = new[] { sourceRecord } }));
    var root = Path.Combine(fixture.Root, "个人 数据");
    Directory.CreateDirectory(root);
    await File.WriteAllTextAsync(Path.Combine(root, "library.json"), "[{\"Id\":\"old-user\",\"Title\":\"旧个人谱\",\"Favorite\":true,\"future_library_value\":{\"keep\":7}}]");
    var store = await fixture.CreateStore();
    var legacy = store.Items.Single(item => item.Id == "old-user");
    var source = store.Items.Single(item => item.SourceId == "legacy-source");
    Check(legacy.WorkId == legacy.Id && legacy.VariantId == legacy.Id && legacy.IsDefaultVariant, "旧个人条目应迁移为独立作品和默认版本。");
    Check(source.WorkId == "legacy-source" && source.VariantId == "legacy-source" && source.IsDefaultVariant && source.Acquisition == "direct-download", "schema2 来源应兼容迁移为单版本作品。");
    Check(store.Catalog["legacy-source"].ExtraData?.ContainsKey("future_catalog_value") == true, "catalog 未知字段应保留在扩展数据中。");
    var persisted = await File.ReadAllTextAsync(Path.Combine(root, "library.json"));
    Check(persisted.Contains("future_library_value") && persisted.Contains("\"keep\": 7") && legacy.Favorite, "保存迁移后的曲库不能丢弃未知字段和收藏。");
}

static async Task WorkGroupingAndSelection()
{
    using var fixture = new Fixture();
    var records = new[]
    {
        new { id = "work-a-default", title = "同名作品", bpm = 90, work_id = "shared-work", variant_id = "default", is_default_variant = true,
            acquisition = "external-import", source_url = "https://www.bilibili.com/video/BV1xx411c7mD", variant = "原版", reference = (object)new { url = "https://www.bilibili.com/video/BV1xx411c7mD", artist = "演奏者", edition = "现场", segment_start_seconds = 12.0, segment_end_seconds = 40.0, future_reference_value = "kept" }, future_catalog_value = "kept" },
        new { id = "work-a-alt", title = "同名作品", bpm = 100, work_id = "shared-work", variant_id = "alternate", is_default_variant = false,
            acquisition = "external-import", source_url = "https://www.douyin.com/video/123", variant = "副歌", reference = (object)null!, future_catalog_value = "kept" },
        new { id = "different-work", title = "同名作品", bpm = 90, work_id = "different-work", variant_id = "different-work", is_default_variant = true,
            acquisition = "external-import", source_url = "https://www.bilibili.com/video/BV1xx411c7mD", variant = "原版", reference = (object)null!, future_catalog_value = "kept" }
    };
    await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { schema_version = 3, songs = records }));
    var store = await fixture.CreateStore();
    var shared = store.GetWorks().Single(work => work.WorkId == "shared-work");
    Check(shared.Variants.Count == 2 && shared.SelectedVariant.SourceId == "work-a-default", "同作品多个来源版本应聚合，默认版本应先选中。");
    Check(store.GetWorks().Count(work => work.Title == "同名作品") == 2, "同名但不同 work_id 的作品必须保持独立。");
    var alternate = shared.Variants.Single(item => item.SourceId == "work-a-alt");
    alternate.Favorite = true;
    alternate.LastPlayed = DateTimeOffset.Parse("2026-09-25T10:00:00Z");
    await store.SaveLibraryAsync();
    store.Settings.SelectedVariants[shared.WorkId] = alternate.Id;
    await store.SaveSettingsAsync();
    var reopened = await fixture.CreateStore();
    var restored = reopened.GetWorks().Single(work => work.WorkId == "shared-work");
    Check(restored.SelectedVariant.Id == alternate.Id, "所选版本应重启后保留。");
    Check(restored.Favorite && restored.LastPlayed == alternate.LastPlayed, "版本收藏和最近使用应显示在所属作品上并持久化。");
    var firstSource = reopened.Catalog["work-a-default"];
    Check(firstSource.Reference?.Artist == "演奏者" && firstSource.Reference.ExtraData?.ContainsKey("future_reference_value") == true, "reference 元数据和未知字段应可读。");
    Check(firstSource.ExtraData?.ContainsKey("future_catalog_value") == true, "schema3 catalog 扩展字段应保留。");
    var external = reopened.Items.Single(item => item.SourceId == "work-a-default");
    Check(!external.Available && external.Subtitle.Contains("需导入"), "没有直链的外部版本应可载入并明确提示导入。");
    await Throws<InvalidDataException>(() => reopened.DownloadAsync(external, CancellationToken.None));
}

static async Task RetiredCatalogPreviews()
{
    using var fixture = new Fixture();
    var records = Enumerable.Range(0, 13).Select(index =>
        SourceRecord($"retired-{index}", $"retired-work-{index}", $"retired-variant-{index}", null)).ToArray();
    await WriteSourceCatalog(fixture, records);
    var store = await fixture.CreateStore();
    var retired = store.Items.Where(item => item.SourceId?.StartsWith("retired-", StringComparison.Ordinal) == true).ToArray();
    var played = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
    foreach (var item in retired)
    {
        item.Favorite = true;
        item.LastPlayed = played;
        store.Settings.SelectedVariants[item.WorkId] = item.Id;
    }
    store.Settings.LastSelectedId = retired[0].Id;
    var local = await store.SaveCopyAsync(Doc(title: "保留的个人曲谱"));
    local.SourceId = "retired-0";
    local.Favorite = true;
    local.LastPlayed = played;
    var localPath = store.ScorePath(local);
    var localHash = LibraryStore.Hash(await File.ReadAllBytesAsync(localPath));
    var missing = new LibraryItem { Id = "source-retired-missing", SourceId = "retired-missing", WorkId = "missing-work",
        FileName = "missing-import.txt", Hash = new string('a', 64), Title = "缺文件的旧导入", Favorite = true, LastPlayed = played };
    var personal = new LibraryItem { Id = "personal-missing", WorkId = "personal-missing", Title = "缺文件的私人记录" };
    var linked = new LibraryItem { Id = "personal-linked", SourceId = "retired-linked", WorkId = "personal-linked", Title = "私人来源记录" };
    store.Items.AddRange([missing, personal, linked]);
    await store.SaveLibraryAsync();
    await store.SaveSettingsAsync();

    await WriteSourceCatalog(fixture);
    var reopened = await fixture.CreateStore();
    var indexPath = Path.Combine(reopened.Root, "library.json");
    var indexBeforeQuery = await File.ReadAllBytesAsync(indexPath);
    var visibleIds = reopened.GetWorks().SelectMany(work => work.AllVariants).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
    Check(retired.All(item => !visibleIds.Contains(item.Id)), "13 个移除的目录占位不能继续出现在软件曲库。");
    Check(new[] { local.Id, missing.Id, personal.Id, linked.Id }.All(visibleIds.Contains), "真实本地曲谱、缺文件的导入和私人记录仍应显示。");
    Check(localHash == LibraryStore.Hash(await File.ReadAllBytesAsync(localPath)), "来源撤下不能改动个人曲谱字节。");
    var indexAfterQuery = await File.ReadAllBytesAsync(indexPath);
    Check(indexBeforeQuery.SequenceEqual(indexAfterQuery), "过滤可见曲库不能写改索引。");
    Check(retired.All(item => reopened.Items.Any(saved => saved.Id == item.Id && saved.Favorite && saved.LastPlayed == played)),
        "旧占位及其收藏、使用记录必须完整保留在索引中。");
    Check(reopened.Settings.LastSelectedId == retired[0].Id && retired.All(item => reopened.Settings.SelectedVariants[item.WorkId] == item.Id),
        "来源撤下不能丢失保存的版本和上次选择。");

    await WriteSourceCatalog(fixture, records);
    var restored = await fixture.CreateStore();
    Check(retired.All(item => restored.GetWorks().SelectMany(work => work.AllVariants).Any(saved => saved.Id == item.Id && saved.Favorite)),
        "来源恢复后应重新显示保留的占位与收藏。");
}

static async Task PublishedSheetReferenceMetadata()
{
    using var fixture = new Fixture();
    var published = SourceRecord("published-sheet", "published-work", "published-variant", new string('a', 64));
    published["source_sha256"] = new string('b', 64);
    published["verification"] = new { rhythm = "reviewed", app_import = "not-tested", log_playback = "not-tested", game = "not-tested" };
    published["review_note"] = "已逐项核对发布谱中的音符。";
    published["review_method"] = "谱面核对和生产解析器验证。";
    published["evidence"] = new[] { new { file = "catalog/calibration-evidence/published-variant.json", sha256 = new string('d', 64) } };
    published["validation"] = new { production_parser = "passed", reviewed_note_count = 25, maximum_parser_boundary_error_seconds = 0.000001 };
    published["reference"] = new Dictionary<string, object?>
    {
        ["type"] = "published-sheet",
        ["url"] = "https://example.com/published-score",
        ["edition"] = "Official score, 2024 edition",
        ["score_sha256"] = new string('c', 64),
        ["midi_sha256"] = new string('e', 64)
    };
    var explicitRecording = SourceRecord("typed-recording", "recording-work", "recording-variant", null);
    explicitRecording["reference"] = new { type = "recording", url = "https://example.com/performance", edition = "2024 performance",
        segment_start_seconds = 12.0, segment_end_seconds = 24.0 };
    var untypedRecording = SourceRecord("untyped-recording", "legacy-recording-work", "legacy-recording-variant", null);
    untypedRecording["reference"] = new { url = "https://example.com/legacy-performance", edition = "Legacy performance" };
    await WriteSourceCatalog(fixture, published, explicitRecording, untypedRecording);

    var store = await fixture.CreateStore();
    var publishedItem = store.Items.Single(item => item.SourceId == "published-sheet");
    Check(publishedItem.Reference?.Type == "published-sheet" && publishedItem.Reference.ScoreSha256 == new string('c', 64) &&
        publishedItem.Reference.MidiSha256 == new string('e', 64), "出版谱类型、必需谱面SHA和可选MIDI SHA必须从参考元数据读取。");
    Check(publishedItem.ReferenceLabel == "参考乐谱：", "出版谱来源应在曲谱信息中显示‘参考乐谱’标签。");
    Check(new LibraryItem { Reference = store.Catalog["typed-recording"].Reference }.ReferenceLabel == "参考录音：" &&
        new LibraryItem { Reference = store.Catalog["untyped-recording"].Reference }.ReferenceLabel == "参考：",
        "显式录音和历史无类型参考应保留各自标签兼容。 ");

    var withoutMidi = SourceRecord("published-no-midi", "published-no-midi-work", "published-no-midi-variant", null);
    withoutMidi["reference"] = new Dictionary<string, object?>
    {
        ["type"] = "published-sheet", ["url"] = "https://example.com/published-score",
        ["edition"] = "Official score, 2024 edition", ["score_sha256"] = new string('c', 64)
    };
    using (var optionalMidiFixture = new Fixture())
    {
        await WriteSourceCatalog(optionalMidiFixture, withoutMidi);
        var optionalMidiStore = await optionalMidiFixture.CreateStore();
        Check(optionalMidiStore.Catalog["published-no-midi"].Reference?.MidiSha256 == null,
            "MIDI SHA 是可选字段，缺失时出版谱仍可载入。");
    }

    var invalidReferences = new List<Dictionary<string, object?>>();
    Dictionary<string, object?> ValidPublishedReference() => new()
    {
        ["type"] = "published-sheet", ["url"] = "https://example.com/published-score",
        ["edition"] = "Official score, 2024 edition", ["score_sha256"] = new string('c', 64)
    };
    var missingScoreHash = ValidPublishedReference(); missingScoreHash.Remove("score_sha256"); invalidReferences.Add(missingScoreHash);
    var invalidScoreHash = ValidPublishedReference(); invalidScoreHash["score_sha256"] = "invalid"; invalidReferences.Add(invalidScoreHash);
    var invalidMidiHash = ValidPublishedReference(); invalidMidiHash["midi_sha256"] = "invalid"; invalidReferences.Add(invalidMidiHash);
    var audioHash = ValidPublishedReference(); audioHash["audio_sha256"] = new string('f', 64); invalidReferences.Add(audioHash);
    var startTime = ValidPublishedReference(); startTime["start_seconds"] = 12.0; invalidReferences.Add(startTime);
    var segmentTime = ValidPublishedReference(); segmentTime["segment_start_seconds"] = 12.0; invalidReferences.Add(segmentTime);
    var aliasType = ValidPublishedReference(); aliasType["type"] = "audio"; invalidReferences.Add(aliasType);
    var insecureUrl = ValidPublishedReference(); insecureUrl["url"] = "http://example.com/published-score"; invalidReferences.Add(insecureUrl);

    foreach (var invalidReference in invalidReferences)
    {
        using var invalidFixture = new Fixture();
        var invalidRecord = SourceRecord("invalid-published", "invalid-published-work", "invalid-published-variant", null);
        invalidRecord["reference"] = invalidReference;
        await WriteSourceCatalog(invalidFixture, invalidRecord);
        await Throws<InvalidDataException>(() => invalidFixture.CreateStore());
    }
}

static async Task LibraryQualityFiltering()
{
    var calibratedHash = new string('a', 64);
    var pendingHash = new string('b', 64);
    var defaultVariant = new LibraryItem { Id = "default", Title = "筛选作品", WorkId = "quality-work", Bpm = 90, IsDefaultVariant = true };
    var calibrated = new LibraryItem { Id = "calibrated", Title = "筛选作品", WorkId = "quality-work", Bpm = 100,
        Available = true, Hash = calibratedHash, VerifiedSha256 = calibratedHash, RhythmStatus = "reviewed" };
    var awaitingImport = new LibraryItem { Id = "awaiting-import", Title = "筛选作品", WorkId = "quality-work", Bpm = 100,
        VerifiedSha256 = pendingHash, RhythmStatus = "reviewed" };
    var mismatched = new LibraryItem { Id = "mismatched", Title = "筛选作品", WorkId = "quality-work", Bpm = 100,
        Available = true, Hash = new string('c', 64), VerifiedSha256 = new string('d', 64), RhythmStatus = "reviewed",
        Favorite = true, LastPlayed = DateTimeOffset.Parse("2026-09-26T10:00:00Z") };
    var invalidHash = new LibraryItem { Id = "invalid-hash", Title = "筛选作品", WorkId = "quality-work", Bpm = 100,
        VerifiedSha256 = "not-a-sha256", RhythmStatus = "reviewed" };
    var unknownSpeed = new LibraryItem { Id = "unknown-speed", Title = "筛选作品", WorkId = "quality-work", Bpm = 120, BpmKnown = false };
    var work = new LibraryWork("quality-work", [defaultVariant, calibrated, awaitingImport, mismatched, invalidHash, unknownSpeed], defaultVariant.Id);
    var reviewed = work.Filter("100", LibraryQualityFilter.ReviewedFragment);
    Check(reviewed is { Variants.Count: 2 } && reviewed.Variants.Any(item => item.Id == calibrated.Id) && reviewed.Variants.Any(item => item.Id == awaitingImport.Id),
        "已复核筛选应接受校准通过的本地版本与目标SHA有效的待导入片段。");
    Check(reviewed!.Variants.All(item => item.Id != mismatched.Id && item.Id != invalidHash.Id) && reviewed.HasTemporarySelection,
        "校准哈希不匹配或目标哈希无效时不得列为已复核，且筛选出的替代版本应标为临时选择。");
    Check(reviewed.Favorite && reviewed.LastPlayed == mismatched.LastPlayed && reviewed.AllVariants.Count == 6,
        "临时筛选仍按整部作品统计收藏与最近记录。");
    Check(work.Filter("100", LibraryQualityFilter.LocalAvailable)?.Variants.Select(item => item.Id).Order().SequenceEqual(new[] { calibrated.Id, mismatched.Id }.Order()) == true,
        "本地可用筛选应按文件是否在本机区分，不替代已复核状态。");
    Check(work.Filter("速度待确认", LibraryQualityFilter.UnknownBpm)?.SelectedVariant.Id == unknownSpeed.Id && work.Filter("999", LibraryQualityFilter.All) == null,
        "未知速度筛选与搜索必须同时匹配同一版本。");

    using var fixture = new Fixture();
    var records = new[]
    {
        new { id = "filter-default", title = "搜索作品", bpm = 90, work_id = "search-work", variant_id = "default", is_default_variant = true,
            acquisition = "external-import", source_url = "https://www.bilibili.com/video/BV1xx411c7mD", variant = "原版" },
        new { id = "filter-alt", title = "搜索作品", bpm = 100, work_id = "search-work", variant_id = "alternate", is_default_variant = false,
            acquisition = "external-import", source_url = "https://www.douyin.com/video/123", variant = "副歌" }
    };
    await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { schema_version = 3, songs = records }));
    var store = await fixture.CreateStore();
    var savedDefault = store.Items.Single(item => item.SourceId == "filter-default");
    store.Settings.LastSelectedId = savedDefault.Id;
    store.Settings.SelectedVariants["search-work"] = savedDefault.Id;
    await store.SaveSettingsAsync();
    var searchMatch = store.GetWorks().Single(item => item.WorkId == "search-work").Filter("100", LibraryQualityFilter.All);
    Check(searchMatch is not null && searchMatch.SelectedVariant.SourceId == "filter-alt" && searchMatch.HasTemporarySelection && searchMatch.PreferredVariantId == savedDefault.Id,
        "搜索命中替代版本时保留原偏好，并识别为临时选择。");
    await store.SaveSettingsAsync();
    var reopened = await fixture.CreateStore();
    Check(reopened.Settings.LastSelectedId == savedDefault.Id && reopened.Settings.SelectedVariants["search-work"] == savedDefault.Id,
        "筛选后关闭并重开时，未被显式更改的上次选择和作品版本偏好保持原值。");
}

static Task CatalogVersionDescription()
{
    var source = new CatalogSong("timed-source", "校对曲", "timed.txt", 120, 0, null, null, "https://example.com/source")
    {
        DurationSeconds = 42.25,
        ReviewNote = "整段升八度并简化了伴奏部分。",
        Reference = new ReferenceMetadata { StartSeconds = 81.15, SegmentStartSeconds = 10 }
    };
    Check(source.Description.Contains("参考录音定位：01:21.15") && source.Description.Contains("参考片段范围：00:10–未注明") &&
        source.Description.Contains("谱面时长：00:42.25") && source.Description.Contains(source.ReviewNote),
        "版本说明应展示已知录音位置、片段边界、谱面时长并保留移调或简化说明。");
    Check(!source.Description.Contains("录音结束") && !source.Description.Contains("02:03"), "未知的源录音结束位置不得被推算或展示。");
    return Task.CompletedTask;
}

static async Task DeferredSourceImport()
{
    using var fixture = new Fixture();
    var record = SourceRecord("deferred-source", "deferred-work", "deferred-variant", null);
    record["verification"] = new { rhythm = "deferred" };
    await WriteSourceCatalog(fixture, record);
    var store = await fixture.CreateStore();
    var path = Path.Combine(fixture.Root, "deferred.txt");
    await File.WriteAllTextAsync(path, ScoreCodec.Write(new ScoreDocument("参考谱", 90, 25, "1 2 3 0 |")));
    var imported = await store.ImportSourceAsync(path, "deferred-source");
    Check(imported.Available && !imported.IsCalibrated && imported.RhythmStatus == "deferred", "暂缓曲谱可作为参考导入，但不能声称校准。");
    await Throws<InvalidDataException>(() => store.ImportReviewedAsync(path, "deferred-source", "deferred-work", "deferred-variant"));
    var reopened = await fixture.CreateStore();
    Check(reopened.Items.Single(item => item.SourceId == "deferred-source").RhythmStatus == "deferred", "暂缓状态必须重启保留。");
}

static async Task ExternalImportAndCalibration()
{
    using var fixture = new Fixture();
    var verifiedText = ScoreCodec.Write(new ScoreDocument("外部曲谱", 90, 25, "1 2 3 0 |"));
    var verifiedBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(verifiedText)).ToArray();
    var verifiedHash = LibraryStore.Hash(verifiedBytes);
    var record = new
    {
        id = "external-reviewed", title = "外部曲谱", filename = "external.txt", bpm = 90,
        work_id = "reviewed-work", variant_id = "reviewed-variant", is_default_variant = true,
        acquisition = "external-import", source_url = "https://www.bilibili.com/video/BV1xx411c7mD",
        verified_sha256 = verifiedHash, source_sha256 = new string('a', 64),
        verification = new { rhythm = "reviewed", app_import = "not-tested", log_playback = "not-tested", game = "not-tested" },
        review_note = "逐音核对 25 个音符。", review_method = "音高、起音和生产解析器逐项比较。",
        evidence = new[] { new { file = "catalog/calibration-evidence/reviewed-variant.json", sha256 = new string('d', 64) } },
        validation = new { production_parser = "passed", reviewed_note_count = 25, independent_attack_supported_notes = 25, maximum_parser_boundary_error_seconds = 0.000001 },
        reference = new { url = "https://www.bilibili.com/video/BV1xx411c7mD", artist = "演奏者", edition = "现场",
            audio_sha256 = new string('b', 64), score_sha256 = new string('c', 64), start_seconds = 12.0 }
    };
    await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { schema_version = 3, songs = new[] { record } }));
    var store = await fixture.CreateStore();
    var sourceItem = store.Items.Single(item => item.SourceId == "external-reviewed");
    Check(!sourceItem.IsCalibrated && sourceItem.VerifiedSha256 == verifiedHash, "不可用版本即使有期望SHA也不能显示已校准。");
    Check(!new LibraryItem { Available = true, RhythmStatus = "reviewed" }.IsCalibrated, "缺少实际和目标SHA时不能显示已校准。");

    foreach (var missingField in new[] { "source_sha256", "review_method", "evidence" })
    {
        using var invalidFixture = new Fixture();
        var invalidRecord = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(record))!;
        invalidRecord.Remove(missingField);
        await File.WriteAllTextAsync(Path.Combine(invalidFixture.Assets, "catalog.json"),
            JsonSerializer.Serialize(new { schema_version = 3, songs = new[] { invalidRecord } }));
        await Throws<InvalidDataException>(() => invalidFixture.CreateStore());
    }

    var wrongPath = await fixture.Source("wrong.txt", ScoreCodec.Write(new ScoreDocument("外部曲谱", 90, 25, "3 2 1 0 |")));
    var scoresBefore = Directory.GetFiles(store.ScoresDirectory, "*.txt").Length;
    await Throws<InvalidDataException>(() => store.ImportReviewedAsync(wrongPath, "external-reviewed", "reviewed-work", "reviewed-variant"));
    Check(!sourceItem.Available && sourceItem.FileName == null && Directory.GetFiles(store.ScoresDirectory, "*.txt").Length == scoresBefore, "错误SHA不能关联来源或写入目标文件。");

    var verifiedPath = Path.Combine(fixture.Root, "verified.txt");
    await File.WriteAllBytesAsync(verifiedPath, verifiedBytes);
    var imported = await store.ImportReviewedAsync(verifiedPath, "external-reviewed", "reviewed-work", "reviewed-variant");
    Check(imported.IsCalibrated && imported.Hash == verifiedHash && imported.Hash == LibraryStore.Hash(await File.ReadAllBytesAsync(store.ScorePath(imported))) && imported.Available, "仅实际文件SHA精确匹配时才能显示已校准，并须保留原始字节。");
    Check(ReferenceEquals(imported, await store.ImportAsync(verifiedPath)), "同SHA文件再次导入应复用版本。");
    var preservedPath = store.ScorePath(imported);
    var tamperedText = ScoreCodec.Write(new ScoreDocument("外部曲谱", 90, 25, "4 5 6 0 |"));
    await File.WriteAllTextAsync(preservedPath, tamperedText, new UTF8Encoding(false));
    await store.ReadDocumentAsync(imported);
    Check(!imported.IsCalibrated && imported.CalibrationRevoked && imported.VerifiedSha256 == null, "读取到本地文件被修改后应立即撤销校准。");
    var corrected = await store.ImportReviewedAsync(verifiedPath, "external-reviewed", "reviewed-work", "reviewed-variant");
    Check(corrected.Id != imported.Id && corrected.IsCalibrated && File.ReadAllText(preservedPath) == tamperedText, "重新导入校正版不得覆盖用户已修改的文件。");
    await store.SaveDocumentAsync(corrected, new ScoreDocument("外部曲谱", 90, 25, "7 6 5 0 |"));
    Check(!corrected.IsCalibrated && corrected.CalibrationRevoked, "保存编辑后的谱面应撤销校准。");
}

static async Task UnknownCatalogBpmImport()
{
    using var fixture = new Fixture();
    var record = SourceRecord("unknown-bpm", "unknown-work", "unknown-variant", null);
    record.Remove("bpm");
    await WriteSourceCatalog(fixture, record);
    var store = await fixture.CreateStore();
    var unavailable = store.Items.Single(item => item.SourceId == "unknown-bpm");
    Check(!store.Catalog["unknown-bpm"].BpmKnown && !unavailable.BpmKnown && unavailable.Bpm == 120,
        "缺失 BPM 的 schema3 外部来源应只在内部使用 120 占位，并标记速度未知。");
    Check(unavailable.Subtitle.Contains("速度待确认") && unavailable.VariantDisplay.Contains("速度待确认"),
        "曲库和版本选择器不能把未知占位显示为 120 BPM。");

    var text = ScoreCodec.Write(new ScoreDocument("已标注速度", 92, 25, "1 2 3 0 |"));
    var imported = await store.ImportSourceAsync(await fixture.Source("known-speed.txt", text), "unknown-bpm");
    Check(imported.Bpm == 92 && imported.BpmKnown && !imported.NeedsBpmConfirmation,
        "未知来源导入带 BPM 的 TXT 后应保存文件中的实际速度。");
    var reopened = await fixture.CreateStore();
    var restored = reopened.Items.Single(item => item.SourceId == "unknown-bpm" && item.Available);
    Check(restored.Bpm == 92 && restored.BpmKnown && (await reopened.ReadDocumentAsync(restored)).Bpm == 92,
        "重启加载仍未知速度的来源元数据时，不得覆盖导入文件的实际 BPM。");

    var nullRecord = SourceRecord("null-bpm", "null-work", "null-variant", null);
    nullRecord["bpm"] = null;
    await WriteSourceCatalog(fixture, nullRecord);
    var nullCatalog = await fixture.CreateStore();
    Check(!nullCatalog.Catalog["null-bpm"].BpmKnown, "显式 null BPM 也应表示未知速度。");
}

static async Task StrictCatalogBpmValidation()
{
    using var fixture = new Fixture();
    foreach (var schemaVersion in new[] { 2, 3 })
    foreach (var bpmValue in new object?[] { null, 19, 301, 90.5, "90", true })
    {
        var record = SourceRecord($"direct-{schemaVersion}-{Guid.NewGuid():N}"[..20], "direct-work", "direct-variant", null);
        record["acquisition"] = "direct-download";
        record["download_url"] = "https://raw.githubusercontent.com/example/repo/main/song.txt";
        record["size"] = 1;
        record["sha256"] = LibraryStore.Hash([49]);
        record["bpm"] = bpmValue;
        await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { schema_version = schemaVersion, songs = new[] { record } }));
        await Throws<InvalidDataException>(() => fixture.CreateStore());
    }
    foreach (var schemaVersion in new[] { 2, 3 })
    {
        var record = SourceRecord($"missing-{schemaVersion}", "direct-work", "direct-variant", null);
        record["acquisition"] = "direct-download";
        record["download_url"] = "https://raw.githubusercontent.com/example/repo/main/song.txt";
        record["size"] = 1;
        record["sha256"] = LibraryStore.Hash([49]);
        record.Remove("bpm");
        await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { schema_version = schemaVersion, songs = new[] { record } }));
        await Throws<InvalidDataException>(() => fixture.CreateStore());
    }
}

static async Task ExplicitSourceImportWithExistingPersonalDuplicate()
{
    using var fixture = new Fixture();
    var scoreText = ScoreCodec.Write(new ScoreDocument("个人版本名", 90, 25, "1 2 3 0 |"));
    var bytes = Encoding.UTF8.GetBytes(scoreText);
    var hash = LibraryStore.Hash(bytes);
    var path = await fixture.Source("before-source.txt", scoreText);
    await WriteSourceCatalog(fixture, SourceRecord("reviewed-source", "reviewed-work", "reviewed-variant", null));

    var store = await fixture.CreateStore();
    var personal = await store.ImportAsync(path);
    personal.Favorite = true;
    personal.LastPlayed = DateTimeOffset.Parse("2026-09-25T10:00:00Z");
    await store.SaveLibraryAsync();
    Check(personal.SourceId == null && personal.Title == "个人版本名", "来源校准前导入的文件应保持为个人曲谱。");

    await WriteSourceCatalog(fixture,
        SourceRecord("reviewed-source", "reviewed-work", "reviewed-variant", hash),
        SourceRecord("same-bytes-other-source", "other-work", "other-variant", hash));
    var reopened = await fixture.CreateStore();
    var restoredPersonal = reopened.Items.Single(item => item.Id == personal.Id);
    var linked = await reopened.ImportReviewedAsync(path, "reviewed-source", "reviewed-work", "reviewed-variant");
    Check(linked.SourceId == "reviewed-source" && linked.Id != restoredPersonal.Id && linked.IsCalibrated,
        "显式校准导入必须建立已绑定、已校准的来源项，不能把同SHA个人曲谱直接返回。");
    Check(restoredPersonal.SourceId == null && restoredPersonal.Title == "个人版本名" && restoredPersonal.Favorite &&
        restoredPersonal.LastPlayed == personal.LastPlayed && File.ReadAllBytes(reopened.ScorePath(restoredPersonal)).SequenceEqual(bytes),
        "建立来源项不能改写个人条目的来源、标题、收藏、最近记录或谱面字节。");
    var sameSourceAgain = await reopened.ImportReviewedAsync(path, "reviewed-source", "reviewed-work", "reviewed-variant");
    Check(ReferenceEquals(linked, sameSourceAgain), "同一来源、同一SHA的重复显式导入应复用已关联项。");

    var otherSource = await reopened.ImportReviewedAsync(path, "same-bytes-other-source", "other-work", "other-variant");
    Check(otherSource.SourceId == "same-bytes-other-source" && otherSource.Id != linked.Id && otherSource.IsCalibrated,
        "相同SHA属于不同来源版本时，应分别保留精确的来源关联。");
}

static async Task CatalogCopyVariantPersistence()
{
    using var fixture = new Fixture();
    await WriteSourceCatalog(fixture, SourceRecord("local-copy-source", "local-copy-work", "local-copy-variant", null));
    var store = await fixture.CreateStore();
    var firstText = ScoreCodec.Write(new ScoreDocument("作品", 90, 25, "1 2 3 0 |"));
    var secondText = ScoreCodec.Write(new ScoreDocument("本机版本", 90, 25, "3 2 1 0 |"));
    var first = await store.ImportSourceAsync(await fixture.Source("first-version.txt", firstText), "local-copy-source");
    var copy = await store.ImportSourceAsync(await fixture.Source("second-version.txt", secondText), "local-copy-source");
    Check(first.IsDefaultVariant && !copy.IsDefaultVariant && copy.VariantLabelOverride == "原版（本机导入）",
        "同一来源的第二份不同内容应成为带本机标签的非默认版本。");

    var reopened = await fixture.CreateStore();
    var variants = reopened.Items.Where(item => item.SourceId == "local-copy-source" && item.Available).ToArray();
    var restoredFirst = variants.Single(item => item.Hash == LibraryStore.Hash(Encoding.UTF8.GetBytes(firstText)));
    var restoredCopy = variants.Single(item => item.Hash == LibraryStore.Hash(Encoding.UTF8.GetBytes(secondText)));
    Check(restoredFirst.IsDefaultVariant && restoredFirst.Variant == "原版",
        "目录默认版本重启后仍须保留默认身份和来源标签。");
    Check(!restoredCopy.IsDefaultVariant && restoredCopy.VariantLabelOverride == "原版（本机导入）" && restoredCopy.Variant == "原版（本机导入）",
        "本机导入副本重启后仍须保留其本机版本标签与非默认身份。");
}

static async Task HttpsUserInfoRejected()
{
    using var fixture = new Fixture();
    var sourceUserInfo = SourceRecord("userinfo-source", "userinfo-work", "userinfo-variant", null);
    sourceUserInfo["source_url"] = "https://user:secret@example.com/source";
    await WriteSourceCatalog(fixture, sourceUserInfo);
    await Throws<InvalidDataException>(() => fixture.CreateStore());

    var referenceUserInfo = SourceRecord("userinfo-reference", "reference-work", "reference-variant", null);
    referenceUserInfo["reference"] = new { url = "https://user@example.com/reference" };
    await WriteSourceCatalog(fixture, referenceUserInfo);
    await Throws<InvalidDataException>(() => fixture.CreateStore());

    var downloadUserInfo = SourceRecord("userinfo-download", "download-work", "download-variant", null);
    downloadUserInfo["acquisition"] = "direct-download";
    downloadUserInfo["source_url"] = "https://example.com/source";
    downloadUserInfo["download_url"] = "https://user:secret@raw.githubusercontent.com/example/repo/main/song.txt";
    downloadUserInfo["sha256"] = new string('a', 64);
    downloadUserInfo["size"] = 1;
    await WriteSourceCatalog(fixture, downloadUserInfo);
    await Throws<InvalidDataException>(() => fixture.CreateStore());
}

static Dictionary<string, object?> SourceRecord(string id, string workId, string variantId, string? verifiedHash)
{
    var record = new Dictionary<string, object?>
    {
        ["id"] = id,
        ["title"] = "目录规范标题",
        ["filename"] = "source.txt",
        ["bpm"] = 90,
        ["work_id"] = workId,
        ["variant_id"] = variantId,
        ["is_default_variant"] = true,
        ["acquisition"] = "external-import",
        ["source_url"] = "https://example.com/source",
        ["variant"] = "原版"
    };
    if (verifiedHash is not null)
    {
        record["verified_sha256"] = verifiedHash;
        record["source_sha256"] = new string('a', 64);
        record["verification"] = new { rhythm = "reviewed" };
        record["review_note"] = "测试逐音审阅记录。";
        record["review_method"] = "测试音高、时值与解析器比较。";
        record["reference"] = new
        {
            url = "https://example.com/reference-recording", edition = "测试录音版本",
            audio_sha256 = new string('b', 64), start_seconds = 0.0
        };
        record["evidence"] = new[]
        {
            new { file = $"catalog/calibration-evidence/{variantId}.json", sha256 = new string('c', 64) }
        };
        record["validation"] = new
        {
            production_parser = "passed", reviewed_note_count = 10,
            maximum_parser_boundary_error_seconds = 0.000001
        };
    }
    return record;
}

static Task WriteSourceCatalog(Fixture fixture, params object[] records) =>
    File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"),
        JsonSerializer.Serialize(new { schema_version = 3, songs = records }));

static async Task GuideMigration()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    foreach (var json in new[] { "{\"StartHotkey\":\"F7\",\"StopHotkey\":\"F9\",\"Volume\":0.35}", "{\"Guide\":null,\"OverlayHotkey\":\"\"}" })
    {
        await File.WriteAllTextAsync(Path.Combine(store.Root, "settings.json"), json);
        var reopened = await fixture.CreateStore();
        var prefs = reopened.Settings.Guide;
        Check(prefs.Mode == "Rhythm" && prefs.Speed == 1 && prefs.Opacity == .75 && prefs.Width == 560 && prefs.Height == 280, "旧设置须有默认跟谱字段。");
        Check(prefs.Left == null && prefs.Top == null && reopened.Settings.OverlayHotkey == "F7", "位置缺省，浮窗热键默认 F7。");
        if (json.Contains("F9")) Check(reopened.Settings.StartHotkey == "F7" && reopened.Settings.StopHotkey == "F9" && reopened.Settings.Volume == .35f, "新增字段不能覆盖现有开始停止热键。");
    }
}

static async Task GuidePersistence()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    store.Settings.Guide = new() { Mode = "WaitForKey", Speed = .75, Opacity = .6, Width = 640, Height = 300, Left = -1400, Top = 120 };
    store.Settings.OverlayHotkey = "Ctrl+F10";
    await store.SaveSettingsAsync();
    var reopened = await fixture.CreateStore(); var prefs = reopened.Settings.Guide;
    Check(prefs.Mode == "WaitForKey" && prefs.Speed == .75 && prefs.Opacity == .6 && prefs.Width == 640 && prefs.Height == 300 && prefs.Left == -1400 && prefs.Top == 120 && reopened.Settings.OverlayHotkey == "Ctrl+F10", "跟谱设置应完整持久化，负屏幕坐标有效。");
    prefs.Mode = "unknown"; prefs.Speed = double.NaN; prefs.Opacity = double.PositiveInfinity; prefs.Width = 10; prefs.Height = 9000; prefs.Left = double.NaN; prefs.Top = double.NegativeInfinity;
    prefs.Normalize();
    Check(prefs.Mode == "Rhythm" && prefs.Speed == 1 && prefs.Opacity == .75 && prefs.Width == 440 && prefs.Height == 1000 && prefs.Left == null && prefs.Top == null, "异常字段应规范化。");
}

static async Task BpmConfirmation()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var item = await store.ImportTextAsync("1 2 3", "未标速度.txt");
    Check(item.Bpm == 120 && item.NeedsBpmConfirmation, "未标速度的旧谱须提示确认。");
    var reopened = await fixture.CreateStore();
    item = reopened.Items.Single(i => i.Id == item.Id);
    var document = await reopened.ReadDocumentAsync(item);
    Check(document.NeedsBpmConfirmation, "重新打开不能静默清除确认状态。");
    await reopened.SaveDocumentAsync(item, document with { Bpm = 90 });
    var saved = await reopened.ReadDocumentAsync(item);
    Check(saved.Bpm == 90 && !saved.Legacy && !saved.NeedsBpmConfirmation, "确认保存须写新格式和实际速度。");
}

static async Task InvalidImports()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var count = store.Items.Count;
    await Throws<ScoreFormatException>(() => store.ImportTextAsync("这是歌词，不是简谱"));
    await Throws<InvalidDataException>(() => store.ImportTextAsync(new string('1', 1_048_577)));
    var bytesPath = await fixture.Source("bad.txt", "1");
    await File.WriteAllBytesAsync(bytesPath, [0xc3, 0x28]);
    await Throws<DecoderFallbackException>(() => store.ImportAsync(bytesPath));
    Check(store.Items.Count == count && Directory.GetFiles(store.ScoresDirectory).Length == count, "失败导入不得新增条目或曲谱。");
}

static async Task DamagedIndexRecovery()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var example = store.Items.Single();
    await store.SaveDocumentAsync(example, Doc("#1 0 【1】", "保留我的改编"));
    var path = store.ScorePath(example);
    var original = await File.ReadAllBytesAsync(path);
    await File.WriteAllTextAsync(Path.Combine(store.Root, "library.json"), "{broken");
    var reopened = await fixture.CreateStore();
    var afterRecovery = await File.ReadAllBytesAsync(path);
    Check(original.SequenceEqual(afterRecovery), "损坏索引恢复不得覆盖已有示例文件。");
    var recovered = await reopened.ReadDocumentAsync(reopened.Items.Single());
    Check(recovered.Title == "保留我的改编" && recovered.Body == "#1 0 【1】", "恢复索引须读取个人示例当前内容。");
    Check(Directory.GetFiles(store.Root, "library.json.broken-*").Length == 1 && reopened.Warnings.Count == 1, "须保留损坏索引副本并提示。");
}

static async Task DamagedExampleRecovery()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var path = store.ScorePath(store.Items.Single());
    await File.WriteAllTextAsync(path, "用户正在手动修改尚未完成");
    await File.WriteAllTextAsync(Path.Combine(store.Root, "library.json"), "{broken");
    var reopened = await fixture.CreateStore();
    Check(await File.ReadAllTextAsync(path) == "用户正在手动修改尚未完成", "即使旧示例无法解析也不得替换文件。");
    Check(reopened.Items.Count == 0 && reopened.Warnings.Count == 2, "无法读取的旧示例须明确提示。");
}

static async Task FailedSave()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var item = await store.ImportTextAsync(Text());
    var path = store.ScorePath(item);
    var before = await File.ReadAllTextAsync(path);
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        await ThrowsStorage(() => store.SaveDocumentAsync(item, Doc("6 7")));
    Check(before == await File.ReadAllTextAsync(path), "失败保存必须保留完整旧文件。");
    Check(!Directory.GetFiles(store.ScoresDirectory, "*.tmp").Any(), "失败保存须清理临时文件。");
    var libraryPath = Path.Combine(store.Root, "library.json");
    using (var locked = new FileStream(libraryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await ThrowsStorage(() => store.SaveLibraryAsync());
    await store.SaveLibraryAsync();
    await store.SaveDocumentAsync(item, Doc("6 7"));
    Check((await store.ReadDocumentAsync(item)).Body == "6 7", "失败后须可正常重试且不能留下锁。");
}

static async Task InvalidPaths()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    foreach (var path in new[] { "../outside.txt", @"..\outside.txt", @"C:\outside.txt", "bad:stream.txt" })
    {
        var item = new LibraryItem { FileName = path };
        await Throws<InvalidDataException>(() => store.SaveDocumentAsync(item, Doc()));
        try { store.ScorePath(item); throw new Exception("无效路径意外通过。"); }
        catch (InvalidDataException) { }
    }
    var index = JsonSerializer.Serialize(new[] { new LibraryItem { FileName = "../outside.txt" } });
    await File.WriteAllTextAsync(Path.Combine(store.Root, "library.json"), index);
    await Throws<InvalidDataException>(async () => { _ = await fixture.CreateStore(); });
    Check(index == await File.ReadAllTextAsync(Path.Combine(store.Root, "library.json")), "非法索引应原样保留。");
}

static async Task OldKitDirectory()
{
    using var fixture = new Fixture();
    var kit = Path.Combine(fixture.Root, "old-kit");
    Directory.CreateDirectory(kit);
    await fixture.Source("old-kit/local/scores/a.txt", "1");
    await fixture.Source("old-kit/local/my-scores/中文/b.txt", "2");
    await fixture.Source("old-kit/local/player/说明.txt", "不是曲谱");
    var found = LibraryStore.EnumerateImportFiles(kit);
    Check(found.Count == 2 && found.All(p => !p.Contains("player")), "旧包仅导入曲谱与个人曲谱目录。");
    var folder = Path.Combine(fixture.Root, "many");
    Directory.CreateDirectory(folder);
    for (var index = 0; index < 501; index++) await File.WriteAllTextAsync(Path.Combine(folder, index + ".txt"), "1");
    await Throws<InvalidDataException>(() => Task.FromResult(LibraryStore.EnumerateImportFiles(folder)));
}

static async Task ConcurrentMetadata()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var item = store.Items.Single();
    var tasks = new List<Task>();
    for (var index = 0; index < 200; index++)
    {
        item.Favorite = index % 2 == 0;
        item.Bpm = 20 + index;
        store.Settings.Volume = index / 200f;
        tasks.Add(store.SaveLibraryAsync());
        tasks.Add(store.SaveSettingsAsync());
    }
    await Task.WhenAll(tasks);
    var persisted = JsonSerializer.Deserialize<List<LibraryItem>>(await File.ReadAllTextAsync(Path.Combine(store.Root, "library.json")))!.Single();
    var settings = JsonSerializer.Deserialize<UserSettings>(await File.ReadAllTextAsync(Path.Combine(store.Root, "settings.json")))!;
    Check(persisted.Bpm == 219 && !persisted.Favorite && settings.Volume == 199 / 200f, "最终JSON须保留最后一次状态。");
    Check(!Directory.GetFiles(store.Root, "*.tmp").Any(), "并发写入不能残留临时JSON。");
}

static async Task DownloadFailures()
{
    using var fixture = new Fixture();
    var store = await fixture.CreateStore();
    var absent = new LibraryItem { SourceId = "missing" };
    await Throws<InvalidDataException>(() => store.DownloadAsync(absent, CancellationToken.None));
    Check(!absent.Available && absent.FileName == null, "来源无效不能标记下载成功。");
    // The cancellation is already signaled; no network request or real song is required.
    var record = new { id = "fixture", title = "原创来源", filename = "fixture.txt", bpm = 120, size = 1,
        sha256 = LibraryStore.Hash([49]), download_url = "https://raw.githubusercontent.com/example/example/main/fixture.txt",
        source_url = "https://github.com/example/example" };
    await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { songs = new[] { record } }));
    store = await fixture.CreateStore();
    var source = store.Items.Single(i => i.SourceId == "fixture");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    await Throws<OperationCanceledException>(() => store.DownloadAsync(source, cancellation.Token));
    Check(!source.Available && source.FileName == null, "取消下载不得落入曲库。");
}

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

static byte[] CommunityBytes(string body = "1_ 2_ 3' 0") => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
{ format = "delta-music", version = 1, title = "原创下载测试", bpm = 120, jianpu = body, sharedBy = "作者", key = "1=G" }));

static async Task WriteCommunityCatalog(Fixture fixture, byte[] bytes, string? hash = null, string? convertedHash = null)
{
    var text = CommunityScoreConverter.ToText(Encoding.UTF8.GetString(bytes));
    var record = new { id = "fixture", title = "原创下载测试", filename = "fixture.txt", bpm = 120,
        size = bytes.Length, sha256 = hash ?? LibraryStore.Hash(bytes), converted_sha256 = convertedHash ?? LibraryStore.Hash(Encoding.UTF8.GetBytes(text)),
        download_format = CommunityScoreConverter.Format, aliases = new[] { "替代曲名", "Alternative" }, variant = "副歌片段", arranger = "作者",
        duration_seconds = ScoreCodec.Parse(ScoreCodec.Read(text)).DurationSeconds,
        download_url = "https://raw.githubusercontent.com/example/example/0123456789012345678901234567890123456789/fixture.deltamusic",
        source_url = "https://github.com/example/example" };
    await File.WriteAllTextAsync(Path.Combine(fixture.Assets, "catalog.json"), JsonSerializer.Serialize(new { songs = new[] { record } }));
}

static async Task ConvertedDownloadAndUpgrade()
{
    using var fixture = new Fixture();
    var bytes = CommunityBytes();
    await WriteCommunityCatalog(fixture, bytes);
    var store = await fixture.CreateStore(() => new HttpClient(new StubDownload(bytes)));
    var item = store.Items.Single(i => i.SourceId == "fixture");
    Check(item.Matches("替代曲名") && item.Matches("alternative") && item.EditionLabel.Contains("副歌片段"), "别名与版本须可查找。");
    await store.DownloadAsync(item, CancellationToken.None);
    var parsed = ScoreCodec.Parse(await store.ReadDocumentAsync(item));
    Check(item.Available && item.DurationSeconds == 1.5 && parsed.Events.Count == 4, "转换后須存为可直接读取的 TXT。");
    Check(item.Hash == LibraryStore.Hash(await File.ReadAllBytesAsync(store.ScorePath(item))), "转换后散列须对应本地 TXT。");
    item.Favorite = true; item.LastPlayed = DateTimeOffset.Parse("2026-09-24T12:00:00Z");
    await store.SaveDocumentAsync(item, Doc("6 7 0", "我的改编"));
    var before = await File.ReadAllBytesAsync(store.ScorePath(item));
    await WriteCommunityCatalog(fixture, CommunityBytes("2 4 6 0"));
    var reopened = await fixture.CreateStore(() => throw new Exception("Offline: network must not be called"));
    var retained = reopened.Items.Single(i => i.SourceId == "fixture");
    await reopened.DownloadAsync(retained, CancellationToken.None);
    Check(retained.Favorite && retained.LastPlayed == item.LastPlayed && retained.Title == "我的改编", "新版目录不得覆盖个人标题、收藏和最近记录。");
    var after = await File.ReadAllBytesAsync(reopened.ScorePath(retained));
    Check(before.SequenceEqual(after), "升级必须保留个人谱面。");
    Check((await reopened.ReadDocumentAsync(retained)).Body == "6 7 0", "离线读取保持个人修改。");
    await File.WriteAllTextAsync(Path.Combine(store.Root, "settings.json"), "{\"Theme\":\"Dark\",\"Guide\":{\"Mode\":\"Rhythm\"}}");
    reopened = await fixture.CreateStore(); await reopened.SaveSettingsAsync();
    Check((await File.ReadAllTextAsync(Path.Combine(store.Root, "settings.json"))).Contains("Guide"), "兼容升级须保留未知设置。");
}

static async Task VerifiedDownloadFailures()
{
    using var fixture = new Fixture();
    var bytes = CommunityBytes();
    await WriteCommunityCatalog(fixture, bytes, new string('0', 64));
    var store = await fixture.CreateStore(() => new HttpClient(new StubDownload(bytes)));
    var item = store.Items.Single(i => i.SourceId == "fixture");
    await Throws<InvalidDataException>(() => store.DownloadAsync(item, CancellationToken.None));
    Check(!item.Available && item.FileName == null, "原始文件散列失败不得保存。");
    await WriteCommunityCatalog(fixture, bytes, convertedHash: new string('0', 64));
    store = await fixture.CreateStore(() => new HttpClient(new StubDownload(bytes)));
    item = store.Items.Single(i => i.SourceId == "fixture");
    await Throws<InvalidDataException>(() => store.DownloadAsync(item, CancellationToken.None));
    Check(!item.Available && item.FileName == null, "转换散列失败不得保存。");
    await WriteCommunityCatalog(fixture, bytes);
    var attempts = 0;
    store = await fixture.CreateStore(() => new HttpClient(new StubDownload(bytes, ++attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
    item = store.Items.Single(i => i.SourceId == "fixture");
    await Throws<HttpRequestException>(() => store.DownloadAsync(item, CancellationToken.None));
    await store.DownloadAsync(item, CancellationToken.None);
    Check(item.Available && attempts == 2, "失败后须能重试。");
    item.Available = false; item.FileName = null;
    await Throws<IOException>(() => store.DownloadAsync(item, CancellationToken.None));
    Check(!item.Available, "索引缺失时不得覆盖已存在的个人文件。");
}
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException("预期异常：" + typeof(T).Name);
}

static async Task ThrowsStorage(Func<Task> action)
{
    try { await action(); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
    throw new InvalidOperationException("预期文件被占用导致保存失败。");
}

sealed class Fixture : IDisposable
{
    private readonly string tempBase = Path.GetFullPath(Path.GetTempPath());
    public string Root { get; }
    public string Assets => Path.Combine(Root, "assets");
    public Fixture()
    {
        Root = Path.Combine(tempBase, "HarmonicaDesk-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Assets, "Examples"));
        File.WriteAllText(Path.Combine(Assets, "catalog.json"), "{\"songs\":[]}");
        File.WriteAllText(Path.Combine(Assets, "Examples", "original.txt"), ScoreCodec.Write(new ScoreDocument("原创测试", 120, 20, "1 2_ 3_ 0 |")));
    }
    public async Task<LibraryStore> CreateStore(Func<HttpClient>? factory = null)
    {
        var store = new LibraryStore(Path.Combine(Root, "个人 数据"), Assets, factory);
        await store.InitializeAsync();
        return store;
    }
    public async Task<string> Source(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        return path;
    }
    public void Dispose()
    {
        var resolved = Path.GetFullPath(Root);
        var parent = Path.GetDirectoryName(resolved);
        if (!string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar), tempBase.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith("HarmonicaDesk-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a directory outside the test fixture root.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}

sealed class StubDownload(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes), RequestMessage = request });
    }
}
