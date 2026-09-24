using Harmonica.Core;

namespace Harmonica.Windows;

public enum GuideMode { Rhythm, WaitForKey }
public enum GuidePhase { Stopped, Countdown, Running, WaitingForKey, Paused, Completed }
[Flags] public enum NoteModifiers { None = 0, Left = 1, Right = 2, Middle = 4 }
public readonly record struct GuideKeyPress(ushort VirtualKey, NoteModifiers Modifiers);
public sealed record GuideProgress(GuidePhase Phase, int EventIndex, double PositionBeats,
    double TotalBeats, double CountdownSeconds, string Feedback);

/// <summary>
/// Single-threaded, clock-driven guide transport. Owns no audio or synthetic-input capability.
/// The desktop dispatcher serializes transport commands and filtered physical input.
/// </summary>
public sealed class GuideEngine
{
    private readonly IPlaybackClock clock;
    private double lastTime, countdownEnd, position, feedbackUntil;
    private int index, matchedIndex = -1;
    private bool started;
    private string feedback = "";
    public ScoreTimeline Timeline { get; }
    public GuideMode Mode { get; private set; }
    public GuidePhase Phase { get; private set; } = GuidePhase.Stopped;
    public double Speed { get; private set; }
    public int RangeStart { get; private set; }
    public int RangeEnd { get; private set; }
    public bool Loop { get; private set; }
    public double BeatsPerSecond => Timeline.Score.Document.Bpm * Speed / 60d;
    public bool IsActive => Phase is GuidePhase.Countdown or GuidePhase.Running or GuidePhase.WaitingForKey;
    public bool IsAnimating => Phase is GuidePhase.Countdown or GuidePhase.Running;

    public GuideEngine(ParsedScore score, GuideMode mode = GuideMode.Rhythm, double speed = 1,
        IPlaybackClock? clock = null)
    {
        Timeline = new(score);
        this.clock = clock ?? new MonotonicPlaybackClock();
        RangeEnd = Timeline.Count - 1;
        SetMode(mode); SetSpeed(speed);
    }
    public GuideProgress Snapshot => new(Phase, index, position, Timeline.TotalBeats,
        Phase == GuidePhase.Countdown ? Math.Max(0, countdownEnd - clock.Seconds) : 0,
        clock.Seconds < feedbackUntil ? feedback : "");

    public void Start()
    {
        RequireInactive();
        if (Phase == GuidePhase.Completed) ResetPosition();
        BeginCountdown(3);
    }
    public void Pause()
    {
        // A countdown may have elapsed before the host can validate the game or arm input.
        // Cancelling that preparation must not consume any unpresented score time.
        if (Phase == GuidePhase.Running) Update();
        if (IsActive) Phase = GuidePhase.Paused;
        feedback = "";
    }
    public void Resume()
    {
        if (Phase != GuidePhase.Paused) throw new InvalidOperationException("请先暂停跟谱。");
        BeginCountdown(started ? 2 / BeatsPerSecond : 3);
    }
    public void Stop() { ResetPosition(); started = false; Phase = GuidePhase.Stopped; }
    private void ResetPosition()
    {
        index = RangeStart; position = Timeline.StartBeat(index); matchedIndex = -1; feedback = "";
        lastTime = clock.Seconds;
    }
    private void BeginCountdown(double seconds)
    {
        started = true;
        lastTime = clock.Seconds; countdownEnd = lastTime + seconds;
        feedback = ""; Phase = GuidePhase.Countdown;
    }
    public void SetSpeed(double speed)
    {
        RequireInactive();
        if (speed is not (.5 or .75 or 1 or 1.25 or 1.5)) throw new ArgumentOutOfRangeException(nameof(speed));
        Speed = speed;
    }
    public void SetMode(GuideMode mode)
    {
        RequireInactive();
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (Mode != mode)
        {
            Mode = mode; matchedIndex = -1;
            index = Math.Clamp(index, RangeStart, RangeEnd); position = Timeline.StartBeat(index);
        }
    }
    public void SeekToEvent(int eventIndex)
    {
        RequireInactive();
        if (eventIndex < RangeStart || eventIndex > RangeEnd) throw new ArgumentOutOfRangeException(nameof(eventIndex));
        index = eventIndex; position = Timeline.StartBeat(index); matchedIndex = -1;
        feedback = ""; Phase = GuidePhase.Paused;
    }
    public void SetLoop(int start, int end, bool enabled)
    {
        RequireInactive();
        if (start < 0 || end < start || end >= Timeline.Count) throw new ArgumentOutOfRangeException(nameof(start));
        RangeStart = start; RangeEnd = end; Loop = enabled;
        ResetPosition(); Phase = GuidePhase.Paused;
    }
    private void RequireInactive()
    {
        if (IsActive) throw new InvalidOperationException("请先暂停跟谱，再调整练习设置。");
    }

    public GuideProgress Update()
    {
        var now = clock.Seconds;
        if (Phase == GuidePhase.Countdown)
        {
            if (now < countdownEnd) return Snapshot;
            lastTime = countdownEnd; Phase = GuidePhase.Running;
        }
        if (Phase != GuidePhase.Running) { lastTime = now; return Snapshot; }
        var target = position + Math.Max(0, now - lastTime) * BeatsPerSecond;
        lastTime = now;
        while (index <= RangeEnd)
        {
            var note = Timeline.Score.Events[index];
            if (Mode == GuideMode.WaitForKey && note.Degree != 0 && matchedIndex != index)
            {
                position = Timeline.StartBeat(index);
                Phase = GuidePhase.WaitingForKey;
                return Snapshot;
            }
            var end = Timeline.EndBeat(index);
            if (target < end - 1e-9) { position = target; return Snapshot; }
            position = end;
            index++;
        }
        if (Loop) { ResetPosition(); BeginCountdown(2 / BeatsPerSecond); }
        else Phase = GuidePhase.Completed;
        return Snapshot;
    }

    public bool Accept(GuideKeyPress press)
    {
        Update();
        if (Mode != GuideMode.WaitForKey || Phase != GuidePhase.WaitingForKey) return false;
        var expected = Expected(Timeline.Score.Events[index]);
        if (press != expected)
        {
            feedback = "当前需要 " + Describe(Timeline.Score.Events[index]);
            feedbackUntil = clock.Seconds + 1.2;
            return false;
        }
        feedback = ""; matchedIndex = index;
        lastTime = clock.Seconds; Phase = GuidePhase.Running;
        return true;
    }
    public static GuideKeyPress Expected(ScoreEvent note)
    {
        var keys = GameKeyMapping.ForNote(note);
        var modifiers = NoteModifiers.None;
        ushort key = 0;
        foreach (var item in keys)
            if (!item.Mouse) key = item.VirtualKey;
            else modifiers |= item.VirtualKey switch { 1 => NoteModifiers.Left, 2 => NoteModifiers.Right, 4 => NoteModifiers.Middle, _ => NoteModifiers.None };
        return new(key, modifiers);
    }
    public static string Describe(ScoreEvent note) => note.Degree == 0 ? "休止 · 松开音键" :
        string.Join("＋", GameKeyMapping.ForNote(note).Select(k => k.Mouse ? k.VirtualKey switch
        { 1 => "左键", 2 => "右键", _ => "中键" } : k.ToString()));
}

/// <summary>Tracks only the eight note keys and mouse modifiers. Repeat downs never produce a new attack.</summary>
public sealed class PhysicalNoteTracker
{
    private readonly HashSet<ushort> down = [];
    public NoteModifiers Modifiers { get; private set; }
    public static readonly IReadOnlyList<ushort> NoteKeys = Array.AsReadOnly(new ushort[] { 90, 88, 67, 86, 66, 78, 77, 188 });
    public void Reset(IEnumerable<ushort>? held = null, NoteModifiers modifiers = NoteModifiers.None)
    {
        down.Clear();
        if (held != null) foreach (var key in held) if (NoteKeys.Contains(key)) down.Add(key);
        Modifiers = modifiers;
    }
    public void Mouse(NoteModifiers button, bool pressed)
    { if (pressed) Modifiers |= button; else Modifiers &= ~button; }
    public GuideKeyPress? Key(ushort key, bool pressed)
    {
        if (!NoteKeys.Contains(key)) return null;
        if (!pressed) { down.Remove(key); return null; }
        return down.Add(key) ? new GuideKeyPress(key, Modifiers) : null;
    }
}
