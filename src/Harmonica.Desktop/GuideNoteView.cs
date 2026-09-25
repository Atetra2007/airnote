using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Harmonica.Core;
using Harmonica.Windows;

namespace Harmonica.Desktop;

/// <summary>Draws only notes intersecting the visible three-second window; never creates per-note controls.</summary>
public sealed class GuideNoteView : FrameworkElement
{
    private static SolidColorBrush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    private static readonly Brush Ink = Brush("#F0F5F3"), Muted = Brush("#A9B6B1"), Line = Brush("#254F6058");
    private static readonly Brush Middle = Brush("#8AD8BC"), Low = Brush("#80B9F3"), High = Brush("#F1BC79"), Sharp = Brush("#E6A5DA");
    private readonly Dictionary<(string, double, Brush, double), FormattedText> textCache = [];
    public ScoreTimeline? Timeline { get; set; }
    public GuideProgress? Progress { get; set; }
    public double Speed { get; set; } = 1;
    public int RangeStart { get; set; }
    public int RangeEnd { get; set; } = int.MaxValue;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth; var height = ActualHeight;
        if (width < 80 || height < 60) return;
        var lane = width / 8; var lineY = height - 39;
        var gridPen = new Pen(Line, 1);
        for (var i = 1; i < 8; i++) dc.DrawLine(gridPen, new(i * lane, 0), new(i * lane, lineY));
        var timeline = Timeline; var progress = Progress;
        var current = progress?.EventIndex ?? 0;
        if (timeline != null && progress != null)
        {
            var bps = timeline.Score.Document.Bpm * Speed / 60;
            var beat = progress.PositionBeats - progress.CountdownSeconds * bps;
            var endBeat = beat + 3 * bps;
            var pixelsPerBeat = lineY / (3 * bps);
            for (var mark = Math.Ceiling(beat); mark < endBeat; mark++)
            {
                var y = lineY - (mark - beat) * pixelsPerBeat;
                dc.DrawLine(gridPen, new(0, y), new(width, y));
            }
            var first = Math.Max(RangeStart, timeline.EventAt(beat));
            var last = Math.Min(RangeEnd, timeline.Count - 1);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, lineY)));
            for (var i = first; i <= last && timeline.StartBeat(i) <= endBeat; i++)
            {
                var note = timeline.Score.Events[i];
                var bottom = lineY - (timeline.StartBeat(i) - beat) * pixelsPerBeat;
                var top = lineY - (timeline.EndBeat(i) - beat) * pixelsPerBeat;
                if (bottom < 0 || top > lineY) continue;
                if (note.Degree == 0)
                {
                    if (bottom - Math.Max(0, top) > 18) Text(dc, "休止", 11, Muted, width / 2, Math.Max(1, top) + 3);
                    continue;
                }
                var expected = GuideEngine.Expected(note);
                var keyIndex = PhysicalNoteTracker.NoteKeys.IndexOf(expected.VirtualKey);
                if (keyIndex < 0) continue;
                var color = note.Octave < 0 ? Low : note.Octave > 0 ? High : Middle;
                var x = keyIndex * lane + 5;
                var rect = new Rect(x, top, Math.Max(4, lane - 10), Math.Max(3, bottom - top - 2));
                dc.PushOpacity(i == current ? .97 : .76);
                dc.DrawRoundedRectangle(color, note.Sharp ? new Pen(Sharp, 2) : null, rect, 5, 5);
                dc.Pop();
                var available = Math.Min(bottom, lineY) - Math.Max(top, 0);
                if (available > 18)
                {
                    var label = (note.Octave switch { -1 => "↓", 1 => "↑", 2 => "↑↑", _ => "" }) + (note.Sharp ? "#" : "") + note.Degree;
                    Text(dc, label, 14, BrushCache.Dark, x + rect.Width / 2, Math.Max(0, top) + 3);
                    if (available > 40 && expected.Modifiers != NoteModifiers.None)
                    {
                        var mods = string.Join("·", new[] { expected.Modifiers.HasFlag(NoteModifiers.Left) ? "左" : "", expected.Modifiers.HasFlag(NoteModifiers.Right) ? "右" : "", expected.Modifiers.HasFlag(NoteModifiers.Middle) ? "中" : "" }.Where(s => s.Length > 0));
                        DrawMouse(dc, x + rect.Width / 2 - 7, Math.Max(0, top) + 24, expected.Modifiers);
                        if (available > 70) Text(dc, mods, 9, BrushCache.Dark, x + rect.Width / 2, Math.Max(0, top) + 45);
                    }
                }
            }
            dc.Pop();
        }
        dc.DrawLine(new Pen(Middle, 2), new(0, lineY), new(width, lineY));
        var activeKey = timeline != null && progress?.Phase != GuidePhase.Completed && current <= RangeEnd && current < timeline.Count && current >= 0 ? GuideEngine.Expected(timeline.Score.Events[current]).VirtualKey : 0;
        for (var i = 0; i < 8; i++)
        {
            var active = PhysicalNoteTracker.NoteKeys[i] == activeKey;
            var rect = new Rect(i * lane + 4, lineY + 7, lane - 8, 26);
            dc.DrawRoundedRectangle(active ? BrushCache.ActiveKey : BrushCache.Key, active ? new Pen(Middle, 1) : null, rect, 5, 5);
            Text(dc, i == 7 ? "," : "ZXCVBNM"[i].ToString(), 13, active ? Middle : Ink, rect.X + rect.Width / 2, lineY + 10);
        }
    }
    private static void DrawMouse(DrawingContext dc, double x, double y, NoteModifiers modifiers)
    {
        var pen = new Pen(BrushCache.Dark, 1);
        dc.DrawRoundedRectangle(null, pen, new Rect(x, y, 14, 18), 5, 5);
        if ((modifiers & NoteModifiers.Left) != 0) dc.DrawRoundedRectangle(BrushCache.Dark, null, new Rect(x + 2, y + 3, 4, 5), 1, 1);
        if ((modifiers & NoteModifiers.Right) != 0) dc.DrawRoundedRectangle(BrushCache.Dark, null, new Rect(x + 8, y + 3, 4, 5), 1, 1);
        if ((modifiers & NoteModifiers.Middle) != 0) dc.DrawRoundedRectangle(BrushCache.Dark, null, new Rect(x + 6, y + 2, 2, 8), 1, 1);
        dc.DrawLine(pen, new(x, y + 10), new(x + 14, y + 10));
    }
    private void Text(DrawingContext dc, string text, double size, Brush color, double center, double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var key = (text, size, color, dpi);
        if (!textCache.TryGetValue(key, out var formatted))
        {
            if (textCache.Count > 400) textCache.Clear();
            formatted = new(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, color, dpi);
            textCache[key] = formatted;
        }
        dc.DrawText(formatted, new(center - formatted.Width / 2, top));
    }
    private static class BrushCache
    {
        public static readonly Brush Dark = Brush("#17252A"), ActiveKey = Brush("#344C43"), Key = Brush("#292F34");
    }
}

internal static class GuideKeyListExtensions
{
    public static int IndexOf(this IReadOnlyList<ushort> keys, ushort value)
    { for (var i = 0; i < keys.Count; i++) if (keys[i] == value) return i; return -1; }
}
