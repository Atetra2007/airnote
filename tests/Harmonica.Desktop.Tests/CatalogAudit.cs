using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Harmonica.Core;
using Harmonica.Desktop.Models;
using Harmonica.Desktop.Services;

internal static class CatalogAudit
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length < 4 || args[0] != "--audit-catalog") throw new ArgumentException("--audit-catalog INDEX CACHE REPORT [--fetch]");
        using var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
        Directory.CreateDirectory(args[2]);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        var results = new List<object>();
        var failures = 0;
        foreach (var song in catalog.RootElement.GetProperty("songs").EnumerateArray())
        {
            var id = song.GetProperty("id").GetString()!;
            try
            {
                if (id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new Exception("Unsafe ID");
                var cache = Path.Combine(args[2], id + ".raw");
                if (!File.Exists(cache) && args.Contains("--fetch"))
                {
                    var uri = new Uri(song.GetProperty("download_url").GetString()!);
                    if (uri.Scheme != "https" || uri.Host != "raw.githubusercontent.com") throw new Exception("Unexpected source");
                    var data = await client.GetByteArrayAsync(uri);
                    await File.WriteAllBytesAsync(cache, data);
                }
                var bytes = await File.ReadAllBytesAsync(cache);
                if (bytes.Length != song.GetProperty("size").GetInt64() || LibraryStore.Hash(bytes) != song.GetProperty("sha256").GetString()) throw new Exception("Source integrity mismatch");
                var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                var converted = song.TryGetProperty("download_format", out var format) && format.GetString() == CommunityScoreConverter.Format;
                if (converted) text = CommunityScoreConverter.ToText(text);
                var doc = ScoreCodec.Read(text, song.GetProperty("filename").GetString()!);
                var parsed = ScoreCodec.Parse(doc);
                if (doc.NeedsBpmConfirmation || doc.Bpm != song.GetProperty("bpm").GetInt32()) throw new Exception("Missing or inconsistent BPM");
                var hash = LibraryStore.Hash(Encoding.UTF8.GetBytes(text));
                if (song.TryGetProperty("converted_sha256", out var expectedHash) && expectedHash.GetString() != hash) throw new Exception("Converted hash mismatch");
                if (song.TryGetProperty("duration_seconds", out var duration) && Math.Abs(duration.GetDouble() - parsed.DurationSeconds) > .001) throw new Exception("Duration mismatch");
                int[] steps = [0, 2, 4, 5, 7, 9, 11];
                var events = parsed.Events.Select(n => new { midi = n.Degree == 0 ? (int?)null : 60 + 12 * n.Octave + steps[n.Degree - 1] + (n.Sharp ? 1 : 0), beats = n.Beats }).ToArray();
                if (events.Any(n => n.midi is < 48 or > 84)) throw new Exception("Unsupported game pitch");
                var eventText = string.Join("\n", events.Select(e => $"{(e.midi?.ToString() ?? "rest")}:{e.beats.ToString("0.######", CultureInfo.InvariantCulture)}"));
                results.Add(new { id, status = "passed", converted_sha256 = converted ? hash : null, events = parsed.Events.Count, beats = parsed.TotalBeats, duration_seconds = parsed.DurationSeconds, event_sha256 = LibraryStore.Hash(Encoding.UTF8.GetBytes(eventText)), event_list = events });
                Console.WriteLine($"PASS {id}: {events.Length} events, {parsed.TotalBeats:0.###} beats, {parsed.DurationSeconds:0.###} s");
            }
            catch (Exception ex) { failures++; results.Add(new { id, status = "failed", error = ex.Message }); Console.WriteLine($"FAIL {id}: {ex.Message}"); }
        }
        await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
