using System.Globalization;
using System.Text;

namespace Harmonica.Desktop.Models;

public enum LibraryView { All, Local, Favorites, Recent }
public enum LibrarySortOrder { Smart, Title, LastPlayed }

/// <summary>Offline, version-aware search. All words must match the same version.</summary>
public sealed class LibrarySearchQuery
{
    private readonly string[] words;
    private readonly string phrase;
    public bool IsEmpty => words.Length == 0;

    public LibrarySearchQuery(string text)
    {
        words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).Where(word => word.Length > 0).Distinct().ToArray();
        phrase = Normalize(text);
    }

    public (int Score, string Hint)? Match(LibraryItem item, IReadOnlyList<string>? workAliases = null)
    {
        if (IsEmpty) return (0, "");
        var fields = new List<(string Text, int Weight, string Hint)> { (Normalize(item.Title), 100, "") };
        var aliases = workAliases ?? item.Aliases;
        fields.AddRange(aliases.Select(alias => (Normalize(alias), 80, "别名：" + alias)));
        fields.Add((Normalize(item.SourceAuthor), 45, "来源作者：" + item.SourceAuthor));
        fields.Add((Normalize(item.Reference?.Artist ?? ""), 45, "参考演奏者：" + item.Reference?.Artist));
        fields.Add((Normalize(item.Variant), 40, "版本：" + item.Variant));
        fields.Add((Normalize(item.BpmKnown ? item.Bpm.ToString(CultureInfo.InvariantCulture) + " BPM" : "速度待确认"), 35, ""));
        var score = 0;
        var hints = new List<string>();
        foreach (var word in words)
        {
            var match = fields.Where(field => field.Text.Contains(word, StringComparison.Ordinal))
                .Select(field => (Score: field.Weight + (field.Text == word ? 20 : field.Text.StartsWith(word, StringComparison.Ordinal) ? 10 : 0), field.Hint))
                .OrderByDescending(field => field.Score).FirstOrDefault();
            if (match.Score == 0) return null;
            score += match.Score;
            if (match.Hint.Length > 0 && !hints.Contains(match.Hint)) hints.Add(match.Hint);
        }
        // An exact song name always precedes a match in somebody else's author/version field.
        if (fields[0].Text == phrase) score += 300;
        else if (aliases.Any(alias => Normalize(alias) == phrase)) score += 200;
        return (score, string.Join(" · ", hints));
    }

    private static string Normalize(string text)
    {
        var normalized = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(character)) normalized.Append(char.ToUpperInvariant(character));
        return normalized.ToString();
    }
}

public static class LibrarySearch
{
    private static readonly StringComparer TitleComparer = StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true);

    public static IReadOnlyList<LibraryWork> Find(IEnumerable<LibraryWork> works, string text = "",
        LibraryView view = LibraryView.All, LibraryQualityFilter quality = LibraryQualityFilter.All,
        LibrarySortOrder sort = LibrarySortOrder.Smart, string? currentItemId = null)
    {
        var query = new LibrarySearchQuery(text);
        var matches = works.Select(work => work.Filter(query, quality, currentItemId, view == LibraryView.Local))
            .OfType<LibraryWork>().Where(work => view switch
            {
                LibraryView.Favorites => work.Favorite,
                LibraryView.Recent => work.LastPlayed != null,
                _ => true
            });
        IOrderedEnumerable<LibraryWork> ordered;
        if (view == LibraryView.Recent || sort == LibrarySortOrder.LastPlayed)
            ordered = matches.OrderByDescending(work => work.LastPlayed).ThenByDescending(work => work.SearchScore);
        else if (sort == LibrarySortOrder.Title)
            ordered = matches.OrderBy(work => work.Title, TitleComparer);
        else
            ordered = matches.OrderByDescending(work => work.SearchScore)
                .ThenByDescending(work => work.Variants.Any(item => item.Available))
                .ThenByDescending(work => work.Favorite)
                .ThenByDescending(work => work.LastPlayed)
                .ThenBy(work => work.Variants.All(item => item.Example))
                .ThenByDescending(work => work.Variants.Any(item => item.IsCalibrated));
        return ordered.ThenBy(work => work.Title, TitleComparer).ThenBy(work => work.WorkId, StringComparer.Ordinal).ToArray();
    }
}
