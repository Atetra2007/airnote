using Xunit;

namespace Harmonica.Core.Tests;

public sealed class ScoreTimelineTests
{
    [Fact]
    public void BoundariesIncludeFullDurationsAndBinarySearchFindsExactOnsets()
    {
        var timeline = new ScoreTimeline(ScoreCodec.Parse(new("Timeline", 120, 150, "1 0:2 3_ 4:4")));
        Assert.Equal(7.5, timeline.TotalBeats);
        Assert.Equal(3, timeline.StartBeat(2)); Assert.Equal(3.5, timeline.EndBeat(2));
        Assert.Equal(0, timeline.EventAt(-2)); Assert.Equal(0, timeline.EventAt(.99));
        Assert.Equal(1, timeline.EventAt(1)); Assert.Equal(2, timeline.EventAt(3));
        Assert.Equal(3, timeline.EventAt(3.5)); Assert.Equal(4, timeline.EventAt(7.5)); Assert.Equal(4, timeline.EventAt(100));
    }
    [Fact]
    public void EventsAreCopiedAndInvalidTimelinesFailEarly()
    {
        var events = new List<ScoreEvent> { new(1, 0, false, 1, 0, 1) };
        var score = new ParsedScore(new("Copy", 120, 20, "1"), events);
        var timeline = new ScoreTimeline(score); events.Clear(); Assert.Single(timeline.Score.Events);
        Assert.Throws<ArgumentException>(() => new ScoreTimeline(score));
        foreach (var value in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentException>(() => new ScoreTimeline(score with { Events = [new(1, 0, false, value, 0, 1)] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.EventAt(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.StartBeat(1));
    }
}
