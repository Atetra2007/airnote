#:project ../../src/Harmonica.Core/Harmonica.Core.csproj

// Optional local integration check. No third-party score content is stored in this repository.
using Harmonica.Core;
using System.Globalization;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("用法：dotnet run --file tests/Harmonica.Core.Tests/LocalSamples.cs -- <本地曲谱目录>");
    return 2;
}

var samples = new[]
{
    (File: "父亲 bpm72.txt", Count: 124, Beats: 68.5),
    (File: "晴天 bpm67.txt", Count: 247, Beats: 132d),
    (File: "雨爱 bpm80-新版.txt", Count: 195, Beats: 124d),
};
foreach (var sample in samples)
{
    var path = Path.Combine(args[0], sample.File);
    var parsed = ScoreCodec.Parse(ScoreCodec.Read(File.ReadAllText(path), sample.File));
    if (parsed.Events.Count != sample.Count || Math.Abs(parsed.TotalBeats - sample.Beats) > .0000001)
        throw new InvalidOperationException($"本地样本 {sample.File} 的事件数或拍数不符合已有离线记录。");
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"PASS {sample.File}: {parsed.Events.Count} events, {parsed.TotalBeats} beats, BPM {parsed.Document.Bpm}, {parsed.DurationSeconds:F6} seconds"));
}
return 0;
