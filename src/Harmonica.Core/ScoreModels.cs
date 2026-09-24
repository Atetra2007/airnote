using System.Globalization;

namespace Harmonica.Core;

public sealed record ScoreDocument(
    string Title,
    int Bpm,
    int GapMs,
    string Body,
    IReadOnlyDictionary<string, string>? ExtraHeaders = null,
    bool NeedsBpmConfirmation = false,
    bool Legacy = false);

public sealed record ScoreEvent(int Degree, int Octave, bool Sharp, double Beats, int Offset, int Length)
{
    public string Label
    {
        get
        {
            var pitch = (Sharp ? "#" : "") + Degree.ToString(CultureInfo.InvariantCulture);
            return Octave switch
            {
                -1 => $"（{pitch}）",
                1 => $"【{pitch}】",
                2 => $"【【{pitch}】】",
                _ => pitch,
            };
        }
    }
}

public sealed record ParsedScore(ScoreDocument Document, IReadOnlyList<ScoreEvent> Events)
{
    public double TotalBeats => Events.Sum(item => item.Beats);
    public double DurationSeconds => TotalBeats * 60d / Document.Bpm;
}

/// <summary>Read errors use file offsets; Parse errors use offsets within ScoreDocument.Body.</summary>
public sealed class ScoreFormatException : FormatException
{
    public int Offset { get; }
    public int Line { get; }
    public int Column { get; }

    public ScoreFormatException(string message, string text, int offset)
        : base(Describe(message, text, offset))
    {
        Offset = Math.Clamp(offset, 0, text.Length);
        (Line, Column) = Locate(text, Offset);
    }

    private static string Describe(string message, string text, int offset)
    {
        var (line, column) = Locate(text, Math.Clamp(offset, 0, text.Length));
        return $"第 {line} 行，第 {column} 列：{message}";
    }

    private static (int Line, int Column) Locate(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\r')
            {
                line++;
                column = 1;
                if (index + 1 < offset && text[index + 1] == '\n') index++;
            }
            else if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else column++;
        }
        return (line, column);
    }
}
