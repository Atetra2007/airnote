namespace Harmonica.Core;

/// <summary>Immutable beat boundaries shared by guide rendering and transport. Gaps do not shorten notes.</summary>
public sealed class ScoreTimeline
{
    private readonly double[] boundaries;
    public ParsedScore Score { get; }
    public int Count => Score.Events.Count;
    public double TotalBeats => boundaries[^1];
    public ScoreTimeline(ParsedScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.Events.Count == 0 || score.Document.Bpm <= 0)
            throw new ArgumentException("曲谱没有有效音符或速度。", nameof(score));
        // Freeze the event list: an editor cannot change a running session underneath its timeline.
        Score = score with { Events = Array.AsReadOnly(score.Events.ToArray()) };
        boundaries = new double[Count + 1];
        for (var i = 0; i < Count; i++)
        {
            var beats = Score.Events[i].Beats;
            if (!double.IsFinite(beats) || beats <= 0) throw new ArgumentException("音符时值无效。", nameof(score));
            boundaries[i + 1] = boundaries[i] + beats;
        }
        if (!double.IsFinite(TotalBeats)) throw new ArgumentException("曲谱总时值无效。", nameof(score));
    }
    public double StartBeat(int index) => boundaries[Checked(index)];
    public double EndBeat(int index) => boundaries[Checked(index) + 1];
    private int Checked(int index) => index >= 0 && index < Count ? index : throw new ArgumentOutOfRangeException(nameof(index));

    /// <returns>The event containing beat, or Count after the final boundary.</returns>
    public int EventAt(double beat)
    {
        if (!double.IsFinite(beat)) throw new ArgumentOutOfRangeException(nameof(beat));
        if (beat < 0) return 0;
        var low = 0; var high = Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (boundaries[mid + 1] <= beat) low = mid + 1; else high = mid;
        }
        return low;
    }
}
