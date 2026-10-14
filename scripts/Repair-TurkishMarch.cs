#:project ../src/Harmonica.Core/Harmonica.Core.csproj
#:project ../src/Harmonica.Windows/Harmonica.Windows.csproj
#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harmonica.Core;
using Harmonica.Windows;

// Creates a local arrangement, not a replacement for the verified source conversion.
// No keyboard, mouse or audio output. Refuses modified or different source scores.
if (args.Length != 2) throw new ArgumentException("Usage: Repair-TurkishMarch.cs SOURCE.txt OUTPUT.txt");
const string sourceHash = "88bbc7ed3a615718b534c55d5b10ef4082109b2cade3ed3caae5cd01f222e9eb";
var bytes = File.ReadAllBytes(args[0]);
if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != sourceHash)
    throw new InvalidDataException("输入不是已核验的原版《土耳其进行曲》，未修改任何文件。");
if (File.Exists(args[1]) || File.Exists(args[1] + ".validation.json"))
    throw new IOException("输出已存在；请选择新文件名，避免覆盖个人修改。");
var original = ScoreCodec.Parse(ScoreCodec.Read(Encoding.UTF8.GetString(bytes)));
var events = original.Events;
var beats = events.Select(n => decimal.Parse(original.Document.Body.Substring(n.Offset, n.Length).Split(':')[1], CultureInfo.InvariantCulture)).ToArray();
var originalBeats = beats.ToArray();
// One-based first grace-note indices. Each group is donor, note, rest, note, rest.
int[] firstNotes = [28, 36, 44, 398, 406, 414, 618, 624, 629];
const decimal graceBeats = .0625m; // 37.5 ms at 100 BPM; 27.5 ms sounding after the source gap.
foreach (var first in firstNotes)
{
    var i = first - 1;
    if (beats[i] != .026042m || beats[i + 2] != .026042m ||
        events[i + 1].Degree != 0 || events[i + 3].Degree != 0 ||
        beats[i + 1] != .002604m || beats[i + 3] != .002604m || events[i - 1].Degree == 0)
        throw new InvalidDataException("装饰音结构不匹配，未写入输出。");
    beats[i - 1] -= 2 * (graceBeats - beats[i]);
    beats[i] = beats[i + 2] = graceBeats;
    if (beats[i - 1] <= 0 || beats.Skip(i - 1).Take(5).Sum() != originalBeats.Skip(i - 1).Take(5).Sum())
        throw new InvalidDataException("局部拍数验证失败。");
}
var body = new StringBuilder();
for (var i = 0; i < events.Count; i++)
    body.Append(events[i].Label).Append(':').Append(beats[i].ToString(CultureInfo.InvariantCulture))
        .Append((i + 1) % 12 == 0 ? " |\n" : " ");
var headers = new Dictionary<string, string>(original.Document.ExtraHeaders!)
{
    ["arrangement"] = "game-grace-v1; 18 grace notes lengthened using time from preceding notes",
    ["original-sha256"] = sourceHash,
};
var document = original.Document with { Title = "土耳其进行曲（游戏适配版）", Body = body.ToString().TrimEnd(), ExtraHeaders = headers };
var text = ScoreCodec.Write(document).Replace("\r\n", "\n") + "\n";
var repaired = ScoreCodec.Parse(ScoreCodec.Read(text));
if (repaired.Document.Bpm != 100 || beats.Sum() != originalBeats.Sum() ||
    !events.Select(n => (n.Degree, n.Octave, n.Sharp)).SequenceEqual(repaired.Events.Select(n => (n.Degree, n.Octave, n.Sharp))) ||
    events.Where(n => n.Degree == 0).Select(n => n.Beats).SequenceEqual(repaired.Events.Where(n => n.Degree == 0).Select(n => n.Beats)) == false)
    throw new InvalidDataException("音高、顺序、休止或总拍数验证失败。");

// Confirm the original failure and exercise the real engine over the complete repair.
var originalOutputCreated = false;
await using (var engine = new PlaybackEngine(() => { originalOutputCreated = true; return new SilentOutput(new AuditClock()); }))
{
    try { await engine.PlayAsync(original, PlaybackMode.Game); throw new Exception("原版未重现短音拒绝。"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("第 28 个音") && ex.Message.Contains("过短")) { }
    if (originalOutputCreated) throw new Exception("原版在校验失败前创建了输出。");
}
var checks = new List<object>();
foreach (var gap in new[] { 0, 60, 250 })
{
    var clock = new AuditClock();
    var output = new SilentOutput(clock);
    await using var engine = new PlaybackEngine(() => output, clock);
    await engine.PlayAsync(repaired, PlaybackMode.Game, gameGapMs: gap);
    if (Math.Abs(clock.Seconds - repaired.DurationSeconds - 3) > .001 ||
        output.Notes != events.Count(n => n.Degree != 0) || output.MinimumHold < .012 - .000001)
        throw new Exception("完整演奏时间轴、音符数量或按住时间验证失败。");
    checks.Add(new { gameGapMs = gap, notes = output.Notes, minimumHoldMs = output.MinimumHold * 1000, secondsWithCountdown = clock.Seconds });
}
var report = new { sourceSha256 = sourceHash, outputSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
    events = events.Count, modifiedGraceNotes = firstNotes.Length * 2, donorNotes = firstNotes.Length,
    bpm = document.Bpm, totalBeats = beats.Sum(), durationSeconds = repaired.DurationSeconds,
    checks, realGameVerified = false, output = "Silent fake output; no keyboard, mouse or audio" };
var reportText = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
using (var file = new FileStream(args[1], FileMode.CreateNew)) file.Write(Encoding.UTF8.GetBytes(text));
File.WriteAllText(args[1] + ".validation.json", reportText);
Console.WriteLine(reportText);

sealed class AuditClock : IPlaybackClock
{
    public double Seconds { get; set; }
    public Task DelayAsync(TimeSpan delay, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Seconds += Math.Max(.0000001, delay.TotalSeconds); return Task.CompletedTask; }
}
sealed class SilentOutput(AuditClock clock) : IPlaybackOutput
{
    private double? on;
    public int Notes { get; private set; }
    public double MinimumHold { get; private set; } = double.PositiveInfinity;
    public Task BeginAsync(PlaybackMode mode, CancellationToken token) => Task.CompletedTask;
    public Task NoteOnAsync(ScoreEvent note, float volume, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (GameKeyMapping.ForNote(note).Any(k => k.Mouse)) clock.Seconds += .024;
        on = clock.Seconds; Notes++;
        return Task.CompletedTask;
    }
    public Task SilenceAsync(CancellationToken token)
    { if (on is double start) MinimumHold = Math.Min(MinimumHold, clock.Seconds - start); on = null; return Task.CompletedTask; }
    public void CheckSafety() { }
    public void SetVolume(float volume) { }
    public Task ReleaseAsync(CancellationToken token) => Task.CompletedTask;
    public void Dispose() { }
}
