using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Harmonica.Core;

/// <summary>Independent implementation of the published HarmonicaPlayer/1 text format.</summary>
public static partial class ScoreCodec
{
    public const string Format = "HarmonicaPlayer/1";
    public const int DefaultGapMs = 20;
    public const int MinimumBpm = 20;
    public const int MaximumBpm = 300;
    public const int MinimumGapMs = 10;
    public const int MaximumGapMs = 5000;
    public const double MaximumBeats = 64;

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "format", "title", "bpm", "gap" };

    public static ScoreDocument Read(string text, string filename = "", int fallbackBpm = 120)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = SplitLines(text);
        var first = 0;
        while (first < lines.Count && string.IsNullOrWhiteSpace(lines[first].Content)) first++;
        var title = Path.GetFileNameWithoutExtension(filename);
        if (first == lines.Count || !lines[first].Content.TrimStart().StartsWith('@'))
        {
            var bpm = BpmFromFilename(filename);
            var resolvedBpm = bpm ?? fallbackBpm;
            ValidateBpm(resolvedBpm, text, 0);
            var body = text.StartsWith('\uFEFF') ? text[1..] : text;
            return new ScoreDocument(title, resolvedBpm, DefaultGapMs, body,
                NeedsBpmConfirmation: bpm is null, Legacy: true);
        }

        var headers = new Dictionary<string, (string Value, int Offset)>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = -1;
        for (var index = first; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line.Content))
            {
                bodyStart = line.End;
                break;
            }
            var trimmed = line.Content.Trim();
            var headerOffset = line.Start + line.Content.IndexOf(trimmed, StringComparison.Ordinal);
            if (!trimmed.StartsWith('@'))
                throw Error("文件头与音符正文之间必须有空行。", text, headerOffset);
            var equal = trimmed.IndexOf('=');
            if (equal < 2)
                throw Error("文件头应写为 @字段=内容。", text, headerOffset);
            var key = trimmed[1..equal].Trim();
            if (!HeaderName().IsMatch(key))
                throw Error("文件头字段名只能使用英文字母、数字、下划线或短横线，并以字母开头。", text, headerOffset);
            if (!headers.TryAdd(key, (trimmed[(equal + 1)..].Trim(), headerOffset)))
                throw Error($"文件头 @{key} 重复。", text, headerOffset);
        }
        if (bodyStart < 0)
            throw Error("文件头与音符正文之间必须有空行。", text, text.Length);
        if (!headers.TryGetValue("format", out var format))
            throw Error("新格式缺少 @format=HarmonicaPlayer/1。", text, lines[first].Start);
        if (!string.Equals(format.Value, Format, StringComparison.Ordinal))
            throw Error($"不支持的曲谱格式：{format.Value}。", text, format.Offset);
        if (!headers.TryGetValue("bpm", out var bpmHeader))
            throw Error("新格式缺少 @bpm。", text, lines[first].Start);
        var parsedBpm = ReadInteger(bpmHeader.Value, "BPM", text, bpmHeader.Offset);
        ValidateBpm(parsedBpm, text, bpmHeader.Offset);
        var gap = DefaultGapMs;
        if (headers.TryGetValue("gap", out var gapHeader))
        {
            gap = ReadInteger(gapHeader.Value, "音符间隔", text, gapHeader.Offset);
            ValidateGap(gap, text, gapHeader.Offset);
        }
        if (headers.TryGetValue("title", out var titleHeader)) title = titleHeader.Value;
        var extras = headers.Where(pair => !ReservedHeaders.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.OrdinalIgnoreCase);
        return new ScoreDocument(title, parsedBpm, gap, text[bodyStart..],
            new ReadOnlyDictionary<string, string>(extras));
    }

    public static ParsedScore Parse(ScoreDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Body);
        ValidateBpm(document.Bpm, document.Body, 0);
        ValidateGap(document.GapMs, document.Body, 0);
        var events = new BodyParser(document.Body).Run();
        foreach (var item in events)
        {
            if (item.Degree != 0 && item.Beats * 60000d / document.Bpm <= document.GapMs)
                throw Error("音符时长必须大于音符间隔；请降低 BPM、减小间隔或修改时值。",
                    document.Body, item.Offset);
        }
        return new ParsedScore(document, events);
    }

    public static string Write(ScoreDocument document)
    {
        _ = Parse(document);
        if (document.Title.Contains('\r') || document.Title.Contains('\n'))
            throw Error("曲名不能包含换行。", document.Body, 0);
        var result = new StringBuilder()
            .Append("@format=").AppendLine(Format)
            .Append("@title=").AppendLine(document.Title)
            .Append("@bpm=").AppendLine(document.Bpm.ToString(CultureInfo.InvariantCulture))
            .Append("@gap=").AppendLine(document.GapMs.ToString(CultureInfo.InvariantCulture));
        var seen = new HashSet<string>(ReservedHeaders, StringComparer.OrdinalIgnoreCase);
        if (document.ExtraHeaders is not null)
        {
            foreach (var (key, value) in document.ExtraHeaders)
            {
                if (!HeaderName().IsMatch(key) || !seen.Add(key))
                    throw Error($"额外文件头字段无效或重复：{key}。", document.Body, 0);
                if (value.Contains('\r') || value.Contains('\n'))
                    throw Error($"文件头 @{key} 的内容不能包含换行。", document.Body, 0);
                result.Append('@').Append(key).Append('=').AppendLine(value);
            }
        }
        result.AppendLine().Append(document.Body);
        return result.ToString();
    }

    private static int? BpmFromFilename(string filename)
    {
        var match = FilenameBpm().Match(Path.GetFileNameWithoutExtension(filename));
        if (!match.Success) return null;
        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bpm)
            ? bpm : int.MaxValue;
    }

    private static int ReadInteger(string value, string name, string text, int offset)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            throw Error($"{name} 必须填写整数，不能留空。", text, offset);
        return number;
    }

    private static void ValidateBpm(int value, string text, int offset)
    {
        if (value is < MinimumBpm or > MaximumBpm)
            throw Error($"BPM 必须在 {MinimumBpm}～{MaximumBpm} 之间。", text, offset);
    }

    private static void ValidateGap(int value, string text, int offset)
    {
        if (value is < MinimumGapMs or > MaximumGapMs)
            throw Error($"音符间隔必须在 {MinimumGapMs}～{MaximumGapMs} 毫秒之间。", text, offset);
    }

    private static ScoreFormatException Error(string message, string text, int offset) => new(message, text, offset);

    private sealed record TextLine(int Start, int End, string Content);

    private static List<TextLine> SplitLines(string text)
    {
        var result = new List<TextLine>();
        var position = text.StartsWith('\uFEFF') ? 1 : 0;
        while (position < text.Length)
        {
            var start = position;
            while (position < text.Length && text[position] is not ('\r' or '\n')) position++;
            var content = text[start..position];
            if (position < text.Length && text[position] == '\r') position++;
            if (position < text.Length && text[position] == '\n') position++;
            result.Add(new TextLine(start, position, content));
        }
        return result;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderName();

    [GeneratedRegex(@"bpm\s*([+-]?[0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FilenameBpm();

    private sealed class BodyParser(string text)
    {
        private readonly List<ScoreEvent> events = [];
        private int position;
        private int last = -1;
        private int underlineCount;
        private bool dotted;
        private bool extended;
        private bool explicitDuration;

        public IReadOnlyList<ScoreEvent> Run()
        {
            ReadSequence(0, null, 0);
            if (events.Count == 0) throw Error("曲谱正文没有音符或休止符。", text, 0);
            return events.AsReadOnly();
        }

        private void ReadSequence(int octave, char? closing, int openingOffset)
        {
            while (position < text.Length)
            {
                var symbol = text[position];
                if (char.IsWhiteSpace(symbol)) { position++; continue; }
                if (symbol == closing) { position++; return; }
                if (symbol is '】' or '）' or ')')
                    throw Error("括号不匹配，请使用完整配对的【】、（）或()。", text, position);
                if (symbol == '|') { position++; last = -1; continue; }
                if (symbol is '【' or '（' or '(')
                {
                    if (octave != 0)
                        throw Error("不支持嵌套音组；双层括号仅支持完整的【【1】】。", text, position);
                    ReadGroup(symbol);
                    continue;
                }
                if (symbol is >= '0' and <= '7' || symbol == '#')
                {
                    var start = position;
                    var sharp = symbol == '#';
                    if (sharp)
                    {
                        position++;
                        if (position >= text.Length || text[position] is < '1' or > '7')
                            throw Error("# 后必须紧接 1～7；升号写在音高括号内部，休止符不能升音。", text, start);
                    }
                    var degree = text[position++] - '0';
                    Add(degree, degree == 0 ? 0 : octave, sharp, start, position - start);
                    continue;
                }
                if (symbol is '_' or '.' or '-' or '—' or ':')
                {
                    ReadDuration(symbol);
                    continue;
                }
                throw Error($"无法识别字符“{symbol}”；正文只接受数字简谱和支持的时值符号。", text, position);
            }
            if (closing is not null)
                throw Error($"括号没有闭合，缺少“{closing}”。", text, openingOffset);
        }

        private void ReadGroup(char opening)
        {
            var start = position;
            if (opening == '【' && text.AsSpan(position).StartsWith("【【1】】", StringComparison.Ordinal))
            {
                position += 5;
                Add(1, 2, false, start, 5);
                return;
            }
            var closing = opening switch { '【' => '】', '（' => '）', _ => ')' };
            var firstEvent = events.Count;
            position++;
            // A new group must contain its own first note before a duration modifier.
            last = -1;
            ReadSequence(opening == '【' ? 1 : -1, closing, start);
            if (events.Count == firstEvent)
                throw Error("音高括号中至少需要一个音符。", text, start);
            if (events.Count == firstEvent + 1)
            {
                var item = events[firstEvent];
                events[firstEvent] = item with { Offset = start, Length = position - start };
            }
        }

        private void Add(int degree, int octave, bool sharp, int start, int length)
        {
            events.Add(new ScoreEvent(degree, octave, sharp, 1, start, length));
            last = events.Count - 1;
            underlineCount = 0;
            dotted = false;
            extended = false;
            explicitDuration = false;
        }

        private void ReadDuration(char symbol)
        {
            var offset = position;
            if (last < 0)
                throw Error("时值符号前必须有音符或休止符，且不能跨过小节线。", text, position);
            var item = events[last];
            if (symbol == ':')
            {
                if (underlineCount != 0 || dotted || extended || explicitDuration)
                    throw Error("冒号指定拍数不能与减时线、附点或延长符混用。", text, offset);
                position++;
                var numberStart = position;
                while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.')) position++;
                var number = text[numberStart..position];
                if (number.Length == 0 || number.EndsWith('.') ||
                    !double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var beats) ||
                    !double.IsFinite(beats) || beats <= 0 || beats > MaximumBeats)
                    throw Error("冒号后应填写大于 0 且不超过 64 的整数或有限小数拍数。", text, numberStart);
                explicitDuration = true;
                events[last] = item with { Beats = beats, Length = position - item.Offset };
                return;
            }
            if (explicitDuration)
                throw Error("冒号指定拍数不能与减时线、附点或延长符混用。", text, offset);
            var changedBeats = item.Beats;
            switch (symbol)
            {
                case '_':
                    if (dotted || extended)
                        throw Error("减时线必须写在附点、延长符之前。", text, offset);
                    if (++underlineCount > 2)
                        throw Error("减时线最多两条，不支持三个下划线。", text, offset);
                    changedBeats /= 2;
                    break;
                case '.':
                    if (dotted || extended)
                        throw Error("只支持一个附点，且附点必须写在延长符之前。", text, offset);
                    dotted = true;
                    changedBeats *= 1.5;
                    break;
                default:
                    extended = true;
                    changedBeats += 1;
                    break;
            }
            if (changedBeats > MaximumBeats)
                throw Error("单个音符或休止符的时值不能超过 64 拍。", text, offset);
            position++;
            events[last] = item with { Beats = changedBeats, Length = position - item.Offset };
        }
    }
}
