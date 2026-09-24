using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Harmonica.Core;

/// <summary>Local conversion of the public delta-music v1 data format. No network or upstream code.</summary>
public static partial class CommunityScoreConverter
{
    public const string Format = "delta-music/1";
    private static readonly int[] Steps = [0, 2, 4, 5, 7, 9, 11];
    private static readonly string[] Chromatic = ["1", "#1", "2", "#2", "3", "4", "#4", "5", "#5", "6", "#6", "7"];

    public static ScoreDocument Read(string json)
    {
        using var input = JsonDocument.Parse(json);
        var root = input.RootElement;
        if (root.GetProperty("format").GetString() != "delta-music" || root.GetProperty("version").GetInt32() != 1)
            throw new FormatException("不支持的社区曲谱版本。");
        var bpm = root.GetProperty("bpm").GetInt32();
        var title = root.GetProperty("title").GetString() ?? "未命名曲谱";
        var text = root.GetProperty("jianpu").GetString() ?? "";
        var events = new List<(int? Midi, decimal Beats)>();
        var tie = false;
        foreach (Match tokenMatch in Tokens().Matches(text))
        {
            var token = tokenMatch.Value.Replace('—', '-').Replace('–', '-').Replace('·', '.').Replace('。', '.');
            if (token == "|") continue;
            if (token == "~")
            {
                if (events.Count == 0 || events[^1].Midi is null || tie) throw Invalid(text, tokenMatch.Index, "连音线位置无效。");
                tie = true; continue;
            }
            if (token.All(c => c == '-'))
            {
                if (events.Count == 0 || tie) throw Invalid(text, tokenMatch.Index, "延音线前缺少音符。");
                events[^1] = (events[^1].Midi, events[^1].Beats + token.Length); continue;
            }
            var match = Note().Match(token);
            if (!match.Success) throw Invalid(text, tokenMatch.Index, "无法转换的音符：" + token);
            var degree = match.Groups["degree"].Value[0] - '0';
            var accidental = match.Groups["accidental"].Value;
            var low = match.Groups["low"].Length;
            var high = match.Groups["high"].Length;
            if (low > 0 && high > 0 || degree == 0 && (low + high > 0 || accidental.Length > 0))
                throw Invalid(text, tokenMatch.Index, "休止或八度标记冲突。");
            int? midi = degree == 0 ? null : 60 + Steps[degree - 1] + 12 * (high - low) + (accidental is "#" or "♯" ? 1 : accidental is "b" or "♭" ? -1 : 0);
            if (midi is < 48 or > 84) throw Invalid(text, tokenMatch.Index, "超出本程序支持的口琴音域。");
            var half = match.Groups["half"].Length;
            var dots = match.Groups["dots"].Length;
            var extra = match.Groups["extra"].Length;
            var explicitBeats = match.Groups["beats"].Value;
            if (explicitBeats.Length > 0 && half + dots + extra > 0) throw Invalid(text, tokenMatch.Index, "精确拍数不能混用时值符号。");
            decimal beats;
            if (explicitBeats.Length > 0)
            {
                if (!decimal.TryParse(explicitBeats, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out beats))
                    throw Invalid(text, tokenMatch.Index, "无效拍数。");
            }
            else beats = (2m - 1m / (1 << dots)) / (1 << half) + extra;
            if (beats <= 0 || beats > 64) throw Invalid(text, tokenMatch.Index, "拍数必须大于 0 且不超过 64。");
            if (tie)
            {
                if (events.Count == 0 || midi is null || events[^1].Midi != midi)
                    throw Invalid(text, tokenMatch.Index, "连音线必须连接相同音高。");
                events[^1] = (midi, events[^1].Beats + beats);
            }
            else events.Add((midi, beats));
            tie = match.Groups["tie"].Success;
            if (events.Count > 25000) throw new FormatException("社区曲谱音符数量超限。");
        }
        if (tie) throw Invalid(text, text.Length, "连音线没有后续音符。");
        var body = new StringBuilder();
        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            if (item.Beats > 64) throw new FormatException("合并后的延音超过 64 拍。");
            body.Append(Pitch(item.Midi)).Append(':').Append(item.Beats.ToString("0.############################", CultureInfo.InvariantCulture));
            body.Append((index + 1) % 12 == 0 ? " |\n" : " ");
        }
        var headers = new Dictionary<string, string>();
        foreach (var key in new[] { "key", "meter", "sharedBy" })
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                headers["source-" + key.ToLowerInvariant()] = value.GetString() ?? "";
        // The source's key label is descriptive; encoded notes are already pitches.
        // Preserve it as metadata instead of transposing the melody a second time.
        var result = new ScoreDocument(title, bpm, 10, body.ToString().TrimEnd(), headers);
        _ = ScoreCodec.Parse(result);
        return result;
    }

    public static string ToText(string json) => ScoreCodec.Write(Read(json)).Replace("\r\n", "\n") + "\n";
    private static string Pitch(int? midi)
    {
        if (midi is null) return "0";
        if (midi == 84) return "【【1】】";
        var pitch = Chromatic[midi.Value % 12];
        return midi.Value < 60 ? "（" + pitch + "）" : midi.Value >= 72 ? "【" + pitch + "】" : pitch;
    }
    private static ScoreFormatException Invalid(string text, int offset, string message) => new(message, text, offset);
    [GeneratedRegex(@"\||[^\s|]+", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();
    [GeneratedRegex(@"^(?<accidental>[#♯b♭]?)(?<low>,{0,2})(?<degree>[0-7])(?<high>'{0,2})(?<half>_{0,3})(?<dots>\.{0,4})(?<extra>-*)(?::(?<beats>[0-9]+(?:\.[0-9]+)?))?(?<tie>~)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Note();
}
