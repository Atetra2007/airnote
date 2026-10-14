using Harmonica.Desktop.Models;

internal static class LibrarySearchChecks
{
    public static Task Run()
    {
        var main = new LibraryItem { Id = "main", WorkId = "song", Title = "晴天", Aliases = ["Sunny Day"],
            SourceAuthor = "小林", Variant = "原版", Bpm = 120, IsDefaultVariant = true };
        var local = new LibraryItem { Id = "local", WorkId = "song", Title = "晴天", Aliases = ["Sunny Day"],
            SourceAuthor = "阿南", Variant = "副歌", Bpm = 90, Available = true, Favorite = true,
            Reference = new() { Artist = "测试演奏者" }, LastPlayed = DateTimeOffset.Parse("2026-10-10T12:00:00Z") };
        var work = new LibraryWork("song", [main, local], main.Id);
        Check(new LibraryWork("song", [main, local]).SelectedVariant == local,
            "Without a saved preference, choose a locally available version before an unavailable default.");
        var authorOnly = new LibraryItem { Id = "author", Title = "别的作品", SourceAuthor = "晴天", Available = true };
        var alias = new LibraryItem { Id = "alias", Title = "另一个名字", Aliases = ["晴天"] };
        var unknown = new LibraryItem { Id = "unknown", Title = "夜曲", BpmKnown = false, Bpm = 120 };
        var works = new[] { new LibraryWork("author", [authorOnly]), new LibraryWork("alias", [alias]),
            work, new LibraryWork("unknown", [unknown]) };

        Check(LibrarySearch.Find(works, "晴天").Select(result => result.WorkId).SequenceEqual(["song", "alias", "author"]),
            "Exact title, alias, then author; local status must not outrank title relevance.");
        Check(work.Filter("副歌 晴天 90", LibraryQualityFilter.All)?.SelectedVariant.Id == local.Id,
            "Unordered words must combine title, edition and tempo in one version.");
        Check(work.Filter("晴天 120 副歌", LibraryQualityFilter.All) == null,
            "Must not combine the tempo of one version with the edition of another.");
        Check(work.Filter("晴天 不存在", LibraryQualityFilter.All) == null, "Every keyword is required.");
        Check(main.Matches("ＳＵＮＮＹ　ＤＡＹ") && main.Matches("sunny-day") && main.Matches("晴 天"),
            "Full width, casing, whitespace and punctuation normalize consistently.");
        Check(new LibraryItem { Title = "Café—Song" }.Matches("cafe song"), "Diacritics should not prevent a name match.");
        Check(main.Matches("小林") && local.Matches("测试演奏者") && local.Matches("副歌"),
            "Source author, reference artist and edition are searchable.");
        Check(!unknown.Matches("120") && unknown.Matches("速度待确认"), "Unknown tempo must not expose placeholder BPM.");
        Check(LibrarySearch.Find(works, "  \t ").Count == 4 && LibrarySearch.Find(works, "—").Count == 4,
            "Whitespace and punctuation-only searches behave as an empty query.");
        var localMatch = LibrarySearch.Find(works, "晴天", LibraryView.Local).Single(result => result.WorkId == "song");
        Check(localMatch.Variants.Count == 1 && localMatch.SelectedVariant == local && localMatch.HasTemporarySelection &&
            localMatch.PreferredVariantId == main.Id && localMatch.VersionBadge == "1/2 版", "Local filter preserves the saved preferred edition.");
        Check(LibrarySearch.Find(works, "120", LibraryView.Local).All(result => result.WorkId != "song"),
            "Local and tempo conditions must be satisfied by the same version.");
        Check(LibrarySearch.Find(works, view: LibraryView.Favorites).Single().WorkId == "song", "Favorites stay at work level.");
        Check(LibrarySearch.Find(works, view: LibraryView.Recent).Single().WorkId == "song", "Recent view excludes unused works.");
        Check(LibrarySearch.Find(works, sort: LibrarySortOrder.LastPlayed).First().WorkId == "song", "Recent sorting places unused works last.");
        Check(work.Filter("sunny", LibraryQualityFilter.All)?.MatchHint.Contains("Sunny Day") == true,
            "Alias results explain why they matched.");
        Check(work.Filter("副歌", LibraryQualityFilter.All)?.MatchHint.Contains("副歌") == true,
            "Edition-only matches are visible in the row.");
        local.Aliases = [];
        Check(LibrarySearch.Find([work], "Sunny Day", LibraryView.Local).Single().SelectedVariant == local,
            "A work's alias must also find a local edition whose source did not repeat the alias.");
        Check(work.Filter("sunny", LibraryQualityFilter.All)?.Variants.Count == 2,
            "Searching a work alias keeps all matching editions available.");
        Check(work.SelectedVariant == main && main.Favorite == false && local.Favorite && main.LastPlayed == null,
            "Search must not change preferences, favorites or playback history.");
        var ordered = LibrarySearch.Find(works, sort: LibrarySortOrder.Title).Select(result => result.WorkId);
        Check(ordered.SequenceEqual(LibrarySearch.Find(works.Reverse(), sort: LibrarySortOrder.Title).Select(result => result.WorkId)),
            "Title ordering is deterministic regardless of import order.");
        return Task.CompletedTask;
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
    }
}
