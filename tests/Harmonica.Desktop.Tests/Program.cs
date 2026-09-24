using System.Text;
using System.Text.Json;
using Harmonica.Core;
using Harmonica.Desktop.Models;
using Harmonica.Desktop.Services;

var scenarios = new (string Name, Func<Task> Run)[]
{
    ("首次初始化与升级保留示例", Initialization),
    ("中文路径复制导入、保留原文件与内容去重", ImportCopy),
    ("同名不同内容和保存副本均不覆盖", SameNameAndCopies),
    ("曲谱、收藏和设置重启持久化", Persistence),
    ("未知速度确认状态与新格式保存", BpmConfirmation),
    ("无效谱面和超限导入不修改曲库", InvalidImports),
    ("损坏索引恢复保留已编辑示例", DamagedIndexRecovery),
    ("损坏索引不替换无法解析的已有示例", DamagedExampleRecovery),
    ("保存失败保留旧文件并可重试", FailedSave),
    ("非法曲库路径拒绝且保留原索引", InvalidPaths),
    ("旧使用包目录与导入数量上限", OldKitDirectory),
    ("并发JSON写入保留最终状态", ConcurrentMetadata),
    ("下载取消与无效来源不写入", DownloadFailures),
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
    public async Task<LibraryStore> CreateStore()
    {
        var store = new LibraryStore(Path.Combine(Root, "个人 数据"), Assets);
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
