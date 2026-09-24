using System.Text.Json;
using Harmonica.Core;
using Xunit;

public class CommunityScoreTests
{
    private static string Input(string body, string key = "1=C") => JsonSerializer.Serialize(new
    { format = "delta-music", version = 1, title = "原创格式检查", bpm = 120, jianpu = body, key, meter = "4/4", sharedBy = "test" });

    [Fact]
    public void PreservesPitchRestsAndDifferentRhythms()
    {
        var result = ScoreCodec.Parse(CommunityScoreConverter.Read(Input(",1_ #4.. b7___ 0:0.125 1'-- #7' 2:0.333333")));
        Assert.Equal(new[] { "（1）", "#4", "#6", "0", "【1】", "【【1】】", "2" }, result.Events.Select(n => n.Label));
        Assert.Equal(new[] { .5, 1.75, .125, .125, 3, 1, .333333 }, result.Events.Select(n => n.Beats));
        Assert.Equal(6.833333, result.TotalBeats, 6);
    }

    [Fact]
    public void TiesMergeOnlyIdenticalPitches()
    {
        var result = ScoreCodec.Parse(CommunityScoreConverter.Read(Input("1_~ | 1. 0 -- b3 ~ #2_")));
        Assert.Equal(new[] { 2d, 3d, 1.5 }, result.Events.Select(n => n.Beats));
        Assert.Equal(3, result.Events.Count);
    }

    [Theory]
    [InlineData("1~ 2")]
    [InlineData("1~")]
    [InlineData("1:0")]
    [InlineData("1_:2")]
    [InlineData(",0")]
    [InlineData("2''")]
    [InlineData("L1'")]
    [InlineData("1:65")]
    [InlineData("5 [unknown]")]
    [InlineData("1:0.001")]
    public void RejectsAmbiguousOrUnplayableInput(string body) => Assert.ThrowsAny<FormatException>(() => CommunityScoreConverter.Read(Input(body)));

    [Fact]
    public void KeyLabelDoesNotTransposeEncodedPitches()
    {
        var result = CommunityScoreConverter.ToText(Input("1 #4 1'", "1=G"));
        Assert.DoesNotContain('\r', result);
        var parsed = ScoreCodec.Parse(ScoreCodec.Read(result));
        Assert.Equal(new[] { "1", "#4", "【1】" }, parsed.Events.Select(n => n.Label));
        Assert.Equal("1=G", parsed.Document.ExtraHeaders!["source-key"]);
        Assert.Equal(1.5, parsed.DurationSeconds);
    }
}
