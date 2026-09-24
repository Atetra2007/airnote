using Harmonica.Core;
using Xunit;

namespace Harmonica.Core.Tests;

public sealed class ScoreCodecTests
{
    private static ParsedScore Parse(string body, int bpm = 120, int gap = 20) =>
        ScoreCodec.Parse(new ScoreDocument("原创测试", bpm, gap, body));

    [Theory]
    [InlineData("5", 1)]
    [InlineData("5_", .5)]
    [InlineData("5__", .25)]
    [InlineData("5.", 1.5)]
    [InlineData("5_.", .75)]
    [InlineData("5__.", .375)]
    [InlineData("5 —", 2)]
    [InlineData("5-", 2)]
    [InlineData("5 — —", 3)]
    [InlineData("5 — — —", 4)]
    [InlineData("5_ —", 1.5)]
    [InlineData("5__ —", 1.25)]
    [InlineData("5. —", 2.5)]
    [InlineData("5_. —", 1.75)]
    [InlineData("5:1.25", 1.25)]
    [InlineData("5:64", 64)]
    [InlineData("0:0.001", .001)]
    [InlineData("【1】:1.25", 1.25)]
    [InlineData("（3）:0.75", .75)]
    [InlineData("【【1】】:1.25", 1.25)]
    [InlineData("【5_】", .5)]
    [InlineData("【5】_", .5)]
    [InlineData("（3.）", 1.5)]
    [InlineData("【【1】】_", .5)]
    public void ReadsPublishedTimingForms(string body, double beats)
    {
        var parsed = Parse(body);
        var item = Assert.Single(parsed.Events);
        Assert.Equal(beats, item.Beats, 8);
        Assert.Equal(beats / 2, parsed.DurationSeconds, 8);
        Assert.Equal(body, body.Substring(item.Offset, item.Length));
    }

    [Fact]
    public void ReadsPitchGroupsSharpsAndRest()
    {
        var result = Parse("1 #2 【123】 （4#56） (7) 【#1】 【【1】】 0000");
        Assert.Equal(new[] { "1", "#2", "【1】", "【2】", "【3】", "（4）", "（#5）", "（6）", "（7）", "【#1】", "【【1】】", "0", "0", "0", "0" },
            result.Events.Select(item => item.Label));
        Assert.Equal(15, result.TotalBeats);
        Assert.Equal(2, result.Events[10].Octave);
    }

    [Fact]
    public void BarsAndWhitespaceAddNoTime()
    {
        Assert.Equal(4, Parse("1 2 |\n\n 3\t 0 |").TotalBeats);
        Assert.Equal(new[] { 1d, 2d }, Parse("【12】—").Events.Select(item => item.Beats));
    }

    [Fact]
    public void IndependentEventsRemainSeparate()
    {
        Assert.Single(Parse("5 —").Events);
        Assert.Equal(2, Parse("5 5").Events.Count);
    }

    [Theory]
    [InlineData("5___", 3)]
    [InlineData("5..", 2)]
    [InlineData("5._", 2)]
    [InlineData("5—.", 2)]
    [InlineData("5—_", 2)]
    [InlineData("5 | — 6", 4)]
    [InlineData("—5", 0)]
    [InlineData("#0", 0)]
    [InlineData("#【1】", 0)]
    [InlineData("5_:1.25", 2)]
    [InlineData("5:1.25_", 6)]
    [InlineData("5:1.25 —", 7)]
    [InlineData("5:1.25.", 2)]
    [InlineData("5:0", 2)]
    [InlineData("5:65", 2)]
    [InlineData("5:-1", 2)]
    [InlineData("5:1/3", 3)]
    [InlineData("5:", 2)]
    [InlineData("5:NaN", 2)]
    [InlineData("[1]", 0)]
    [InlineData("1*", 1)]
    [InlineData("1,", 1)]
    [InlineData("8", 0)]
    [InlineData("（1)", 2)]
    [InlineData("【1", 0)]
    [InlineData("【】", 0)]
    [InlineData("【【2】】", 1)]
    [InlineData("【【#1】】", 1)]
    [InlineData("【【1_】】", 1)]
    [InlineData("((1))", 1)]
    [InlineData("5【_1】", 2)]
    [InlineData("", 0)]
    public void InvalidBodyExplainsExactPosition(string body, int expectedOffset)
    {
        var error = Assert.Throws<ScoreFormatException>(() => Parse(body));
        Assert.Equal(expectedOffset, error.Offset);
        Assert.Equal(1, error.Line);
        Assert.Equal(expectedOffset + 1, error.Column);
        Assert.Contains("第 1 行", error.Message);
    }

    [Theory]
    [InlineData("1\n2 ?", 4, 2, 3)]
    [InlineData("1\r\n2 ?", 5, 2, 3)]
    [InlineData("1\r2 ?", 4, 2, 3)]
    public void ErrorLocationsHandleAllCommonLineEndings(string body, int offset, int line, int column)
    {
        var error = Assert.Throws<ScoreFormatException>(() => Parse(body));
        Assert.Equal(offset, error.Offset);
        Assert.Equal(line, error.Line);
        Assert.Equal(column, error.Column);
    }

    [Fact]
    public void ExtensionCannotExceed64Beats()
    {
        Assert.Equal(64, Parse("1" + new string('-', 63)).TotalBeats);
        Assert.Throws<ScoreFormatException>(() => Parse("1" + new string('-', 64)));
    }

    [Fact]
    public void GapMustLeavePositiveSoundDurationButRestsMayBeShort()
    {
        Assert.Throws<ScoreFormatException>(() => Parse("1:0.04", gap: 20));
        Assert.Throws<ScoreFormatException>(() => Parse("1:0.01", gap: 20));
        Assert.Equal(.04, Parse("0:0.04", gap: 20).TotalBeats);
        Assert.Single(Parse("1:0.041", gap: 20).Events);
    }

    [Fact]
    public void HeaderBpmOverridesFilenameAndFallback()
    {
        var doc = ScoreCodec.Read("@format=HarmonicaPlayer/1\n@bpm=72\n\n1", "原创 bpm180.txt", 90);
        Assert.Equal(72, doc.Bpm);
        Assert.Equal(20, doc.GapMs);
        Assert.False(doc.NeedsBpmConfirmation);
        Assert.False(doc.Legacy);
        Assert.Equal("原创 bpm180", doc.Title);
    }

    [Fact]
    public void LegacyBpmUsesFilenameThenExplicitConfirmation()
    {
        var filename = ScoreCodec.Read("1 2", "中文 BpM67.txt", 99);
        Assert.Equal(67, filename.Bpm);
        Assert.False(filename.NeedsBpmConfirmation);
        Assert.True(filename.Legacy);
        Assert.Equal(20, filename.GapMs);
        var fallback = ScoreCodec.Read("1", "无速度.txt", 99);
        Assert.Equal(99, fallback.Bpm);
        Assert.True(fallback.NeedsBpmConfirmation);
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Read("1", "坏速度 bpm999.txt"));
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Read("1", "坏速度 bpm12.5.txt"));
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Read("1", "坏速度 bpm-72.txt"));
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Read("1", fallbackBpm: 0));
    }

    [Fact]
    public void ReadWritePreservesBodyAndUnknownHeaders()
    {
        const string body = "（1） 1 【1】\r\n0:2 #5__. |\r\n";
        var doc = ScoreCodec.Read("\uFEFF@format=HarmonicaPlayer/1\r\n@title=测试标题\r\n@bpm=90\r\n@gap=25\r\n@source=https://example.org/score\r\n\r\n" + body);
        Assert.Equal(body, doc.Body);
        Assert.Equal("https://example.org/score", doc.ExtraHeaders!["source"]);
        var roundtrip = ScoreCodec.Read(ScoreCodec.Write(doc));
        Assert.Equal(doc.Title, roundtrip.Title);
        Assert.Equal(doc.Body, roundtrip.Body);
        Assert.Equal(doc.Bpm, roundtrip.Bpm);
        Assert.Equal(doc.GapMs, roundtrip.GapMs);
        Assert.Equal(doc.ExtraHeaders, roundtrip.ExtraHeaders);
        Assert.Equal(ScoreCodec.Parse(doc).Events, ScoreCodec.Parse(roundtrip).Events);
    }

    [Fact]
    public void SaveUpgradesLegacyFormatWithoutDiscardingBody()
    {
        var legacy = ScoreCodec.Read("1 2_ 3_", "我的曲谱 bpm100.txt");
        var saved = ScoreCodec.Read(ScoreCodec.Write(legacy));
        Assert.False(saved.Legacy);
        Assert.False(saved.NeedsBpmConfirmation);
        Assert.Equal(100, saved.Bpm);
        Assert.Equal(legacy.Body, saved.Body);
    }

    [Theory]
    [InlineData("@format=HarmonicaPlayer/2\n@bpm=120\n\n1", 0)]
    [InlineData("@format=HarmonicaPlayer/1\n\n1", 0)]
    [InlineData("@bpm=120\n\n1", 0)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=0\n\n1", 26)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=120.5\n\n1", 26)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=120\n1", 35)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=120\n@bpm=90\n\n1", 35)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=120\n@gap=\n\n1", 35)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=120\n@gap=9\n\n1", 35)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=120\n@gap=5001\n\n1", 35)]
    [InlineData("@format=HarmonicaPlayer/1\n@bpm=120\n@source=a\n@SOURCE=b\n\n1", 45)]
    public void InvalidHeadersAreNeverSilentlyRepaired(string text, int offset)
    {
        var error = Assert.Throws<ScoreFormatException>(() => ScoreCodec.Read(text));
        Assert.Equal(offset, error.Offset);
    }

    [Fact]
    public void HeadersNeedActualBlankSeparator()
    {
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Read("@format=HarmonicaPlayer/1\n@bpm=120\n"));
        var doc = ScoreCodec.Read("@format=HarmonicaPlayer/1\n@bpm=120\n\n");
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Parse(doc));
    }

    [Fact]
    public void WriteRejectsHeaderInjectionAndInvalidBody()
    {
        var doc = new ScoreDocument("测试", 120, 20, "1");
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Write(doc with { Title = "歌\n@bpm=300" }));
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Write(doc with { Body = "x" }));
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Write(doc with { ExtraHeaders = new Dictionary<string, string> { ["bpm"] = "300" } }));
        Assert.Throws<ScoreFormatException>(() => ScoreCodec.Write(doc with { ExtraHeaders = new Dictionary<string, string> { ["source"] = "a\n@bpm=300" } }));
    }

    [Theory]
    [InlineData(19, 20)]
    [InlineData(301, 20)]
    [InlineData(120, 9)]
    [InlineData(120, 5001)]
    public void EditedDocumentsStillEnforceParameterRanges(int bpm, int gap)
    {
        Assert.Throws<ScoreFormatException>(() => Parse("0:64", bpm, gap));
    }

    [Fact]
    public void HeaderBoundsAreInclusiveAndShortRestsRemainValid()
    {
        Assert.Single(Parse("0", 20, 10).Events);
        Assert.Single(Parse("0", 300, 5000).Events);
    }

    [Theory]
    [InlineData("01-timing-check.txt", 120, 20, 11, 11, 5.5)]
    [InlineData("02-stop-check.txt", 60, 20, 5, 32, 32)]
    [InlineData("03-import-save-check.txt", 90, 25, 8, 8.25, 5.5)]
    public void OriginalBundledScoresHaveExpectedTimelines(string filename, int bpm, int gap, int count, double beats, double seconds)
    {
        var doc = ScoreCodec.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OriginalScores", filename)), filename);
        var result = ScoreCodec.Parse(doc);
        Assert.Equal(bpm, doc.Bpm);
        Assert.Equal(gap, doc.GapMs);
        Assert.Equal(count, result.Events.Count);
        Assert.Equal(beats, result.TotalBeats, 8);
        Assert.Equal(seconds, result.DurationSeconds, 8);
    }
}
