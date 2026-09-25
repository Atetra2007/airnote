using Harmonica.Core;
using Harmonica.Windows;

internal static class GuideTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All => new (string, Action)[]
    {
        ("Guide canonical octave/sharp/mouse combinations", Canonical),
        ("Guide countdown cancellation and restart", Countdown),
        ("Guide expired preparation can pause without consuming score time", GuardedCountdown),
        ("Guide rhythm uses complete beats, ignores GapMs and keyboard", Rhythm),
        ("Guide waits at each onset and rejects wrong/extra modifiers", Wait),
        ("Guide rests advance; late frames never skip an unplayed note", Rests),
        ("Guide fresh edges, repeat suppression and consecutive same notes", Edges),
        ("Guide held keys at start/resume cannot pass; mouse may remain held", HeldKeys),
        ("Guide pause/resume preserves a matched long note and elapsed position", PauseResume),
        ("Guide first seek/range gets three seconds, later resume gets two beats", Preparation),
        ("Guide speed always derives from original BPM", Speed),
        ("Guide seek/mode changes require pause and clear old match", SeekMode),
        ("Guide inclusive A/B and single-note loop rearm", Loops),
        ("Guide stopped/completed input cannot advance; new song starts clean", Sessions),
        ("Guide long-score traversal and bounded waiting", LongScore),
    }.Select(t => (t.Item1, (Func<Task>)(() => { t.Item2(); return Task.CompletedTask; })));

    private static ParsedScore Score(string body, int bpm = 120, int gap = 20) => ScoreCodec.Parse(new("Guide test", bpm, gap, body));
    private static GuideEngine Engine(string body, FakeClock clock, GuideMode mode = GuideMode.WaitForKey, int bpm = 120, int gap = 20) => new(Score(body, bpm, gap), mode, clock: clock);
    private static void Begin(GuideEngine guide, FakeClock clock) { guide.Start(); clock.Seconds += 3; guide.Update(); }
    private static GuideKeyPress Key(char key, NoteModifiers modifiers = NoteModifiers.None) => new((ushort)key, modifiers);
    private static void Check(bool value, string message = "Guide assertion failed") { if (!value) throw new Exception(message); }
    private static void Eq<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception($"Expected {a}, got {b}"); }
    private static void Near(double a, double b) => Check(Math.Abs(a - b) < .000001, $"Expected {a}, got {b}");
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Active transport should reject edits"); }

    private static void Canonical()
    {
        var cases = new[]
        {
            ("1", Key('Z'), "Z"), ("7", Key('M'), "M"),
            ("（1）", Key('Z', NoteModifiers.Left), "左键＋Z"),
            ("【1】", new GuideKeyPress(188, NoteModifiers.None), ","),
            ("【2】", Key('X', NoteModifiers.Right), "右键＋X"),
            ("【【1】】", new GuideKeyPress(188, NoteModifiers.Right), "右键＋,"),
            ("（#3）", Key('C', NoteModifiers.Left | NoteModifiers.Middle), "左键＋中键＋C"),
            ("【#2】", Key('X', NoteModifiers.Right | NoteModifiers.Middle), "右键＋中键＋X"),
            ("#1", Key('Z', NoteModifiers.Middle), "中键＋Z")
        };
        foreach (var (body, expected, description) in cases)
        {
            var note = Score(body).Events[0]; Eq(expected, GuideEngine.Expected(note)); Eq(description, GuideEngine.Describe(note));
            var clock = new FakeClock(); var g = Engine(body, clock); Begin(g, clock); Check(g.Accept(expected));
        }
        Eq("休止 · 松开音键", GuideEngine.Describe(Score("0").Events[0]));
    }
    private static void Countdown()
    {
        var c = new FakeClock(); var g = Engine("1 2", c); g.Start(); c.Seconds = 2.99;
        Eq(GuidePhase.Countdown, g.Update().Phase); Check(!g.Accept(Key('Z')));
        g.Stop(); c.Seconds = 100; Eq(GuidePhase.Stopped, g.Update().Phase); Near(0, g.Snapshot.PositionBeats);
        g.Start(); Near(3, g.Snapshot.CountdownSeconds); c.Seconds += 3; Eq(GuidePhase.WaitingForKey, g.Update().Phase);
    }
    private static void Rhythm()
    {
        var c = new FakeClock(); var g = Engine("1 2", c, GuideMode.Rhythm, gap: 150); Begin(g, c);
        Check(!g.Accept(Key('Z'))); c.Seconds = 3.49; Eq(0, g.Update().EventIndex); Near(.98, g.Snapshot.PositionBeats);
        c.Seconds = 3.5; Eq(1, g.Update().EventIndex); c.Seconds = 4; Eq(GuidePhase.Completed, g.Update().Phase); Near(2, g.Snapshot.PositionBeats);
    }
    private static void GuardedCountdown()
    {
        foreach (var mode in new[] { GuideMode.Rhythm, GuideMode.WaitForKey })
        {
            var c = new FakeClock(); var g = Engine("0 1 2", c, mode);
            g.Start(); c.Seconds = 100; g.Pause();
            Eq(GuidePhase.Paused, g.Phase); Eq(0, g.Snapshot.EventIndex); Near(0, g.Snapshot.PositionBeats);
            g.Resume(); Near(1, g.Snapshot.CountdownSeconds);
            c.Seconds += 1; g.Update(); Near(0, g.Snapshot.PositionBeats);
            c.Seconds += .25; g.Pause(); Near(.5, g.Snapshot.PositionBeats);
        }
    }
    private static void Wait()
    {
        var c = new FakeClock(); var g = Engine("（#3） 2", c); Begin(g, c);
        Check(!g.Accept(Key('X'))); Check(!g.Accept(Key('C', NoteModifiers.Left)));
        Check(!g.Accept(Key('C', NoteModifiers.Left | NoteModifiers.Middle | NoteModifiers.Right)));
        Check(g.Snapshot.Feedback.Contains("左键＋中键＋C")); c.Seconds += 20;
        Eq(GuidePhase.WaitingForKey, g.Update().Phase); Near(0, g.Snapshot.PositionBeats); Eq("", g.Snapshot.Feedback);
        Check(g.Accept(Key('C', NoteModifiers.Left | NoteModifiers.Middle))); c.Seconds += .5;
        Eq(1, g.Update().EventIndex); Eq(GuidePhase.WaitingForKey, g.Phase);
    }
    private static void Rests()
    {
        var c = new FakeClock(); var g = Engine("0 1 0:2 2", c); Begin(g, c);
        Eq(GuidePhase.Running, g.Phase); Check(!g.Accept(Key('Z')));
        c.Seconds = 100; g.Update(); Eq(1, g.Snapshot.EventIndex); Near(1, g.Snapshot.PositionBeats);
        Check(g.Accept(Key('Z'))); c.Seconds += .75; Eq(2, g.Update().EventIndex); Near(2.5, g.Snapshot.PositionBeats);
        c.Seconds += 100; Eq(3, g.Update().EventIndex); Near(4, g.Snapshot.PositionBeats); Eq(GuidePhase.WaitingForKey, g.Phase);
    }
    private static void Edges()
    {
        var c = new FakeClock(); var g = Engine("1 1", c); var t = new PhysicalNoteTracker(); Begin(g, c);
        Check(t.Key((ushort)'A', true) == null); Check(g.Accept(t.Key((ushort)'Z', true)!.Value));
        c.Seconds += .5; g.Update(); Check(t.Key((ushort)'Z', true) == null); Eq(GuidePhase.WaitingForKey, g.Phase);
        t.Key((ushort)'Z', false); Check(g.Accept(t.Key((ushort)'Z', true)!.Value)); c.Seconds += .5; Eq(GuidePhase.Completed, g.Update().Phase);
    }
    private static void HeldKeys()
    {
        var t = new PhysicalNoteTracker(); t.Reset([(ushort)'C'], NoteModifiers.Left | NoteModifiers.Middle);
        Check(t.Key((ushort)'C', true) == null); t.Key((ushort)'C', false);
        Eq(Key('C', NoteModifiers.Left | NoteModifiers.Middle), t.Key((ushort)'C', true)!.Value);
        t.Key((ushort)'C', false); Eq(Key('C', NoteModifiers.Left | NoteModifiers.Middle), t.Key((ushort)'C', true)!.Value);
        t.Reset([(ushort)'C'], NoteModifiers.Middle); Check(t.Key((ushort)'C', true) == null);
        t.Mouse(NoteModifiers.Middle, false); t.Mouse(NoteModifiers.Right, true); Eq(NoteModifiers.Right, t.Modifiers);
        t.Reset(); Eq(NoteModifiers.None, t.Modifiers);
    }
    private static void PauseResume()
    {
        var c = new FakeClock(); var g = Engine("1:4 2", c); Begin(g, c); Check(g.Accept(Key('Z')));
        c.Seconds += .75; g.Pause(); Near(1.5, g.Snapshot.PositionBeats); c.Seconds += 60;
        g.Update(); Near(1.5, g.Snapshot.PositionBeats); g.Resume(); Near(1, g.Snapshot.CountdownSeconds);
        c.Seconds += 1; Eq(GuidePhase.Running, g.Update().Phase); Near(1.5, g.Snapshot.PositionBeats);
        c.Seconds += 1.25; Eq(1, g.Update().EventIndex); Eq(GuidePhase.WaitingForKey, g.Phase);
        g.Pause(); c.Seconds += 20; g.Resume(); c.Seconds += 1; Eq(GuidePhase.WaitingForKey, g.Update().Phase);
    }
    private static void Preparation()
    {
        var c = new FakeClock(); var g = Engine("1 2", c); g.SetLoop(1, 1, true); g.Resume(); Near(3, g.Snapshot.CountdownSeconds);
        c.Seconds = 3; g.Update(); Check(g.Accept(Key('X'))); g.Pause(); g.Resume(); Near(1, g.Snapshot.CountdownSeconds);
        g.Stop(); g.SeekToEvent(1); g.Resume(); Near(3, g.Snapshot.CountdownSeconds);
    }
    private static void Speed()
    {
        var c = new FakeClock(); var g = Engine("1:32", c, GuideMode.Rhythm);
        foreach (var speed in new[] { .5, .75, 1, 1.25, 1.5 })
        {
            g.Stop(); g.SetSpeed(speed); Begin(g, c); c.Seconds += 1; g.Update(); Near(2 * speed, g.Snapshot.PositionBeats);
            Eq(120, g.Timeline.Score.Document.Bpm); Reject(() => g.SetSpeed(1));
        }
    }
    private static void SeekMode()
    {
        var c = new FakeClock(); var g = Engine("1:4 2", c); Begin(g, c); Check(g.Accept(Key('Z')));
        Reject(() => g.SeekToEvent(1)); Reject(() => g.SetMode(GuideMode.Rhythm)); Reject(() => g.SetLoop(0, 0, true));
        g.Pause(); g.SeekToEvent(0); g.Resume(); c.Seconds += 1; Eq(GuidePhase.WaitingForKey, g.Update().Phase);
        g.Pause(); g.SeekToEvent(1); Near(4, g.Snapshot.PositionBeats); g.SetMode(GuideMode.Rhythm); g.SetSpeed(.5);
        g.Resume(); c.Seconds += 2.25; g.Update(); Near(4.25, g.Snapshot.PositionBeats);
    }
    private static void Loops()
    {
        var c = new FakeClock(); var g = Engine("1 2:2 3 4", c, GuideMode.Rhythm); g.SetLoop(1, 2, false); g.Start();
        c.Seconds = 4.5; Eq(GuidePhase.Completed, g.Update().Phase); Near(4, g.Snapshot.PositionBeats);
        g.Stop(); Eq(1, g.Snapshot.EventIndex); Near(1, g.Snapshot.PositionBeats);
        g.SetMode(GuideMode.WaitForKey); g.SetLoop(1, 1, true); g.Start(); c.Seconds += 3; g.Update();
        Check(g.Accept(Key('X'))); c.Seconds += 1; g.Update(); Eq(GuidePhase.Countdown, g.Phase); Near(1, g.Snapshot.CountdownSeconds);
        Check(!g.Accept(Key('X'))); c.Seconds += 1; Eq(GuidePhase.WaitingForKey, g.Update().Phase); Eq(1, g.Snapshot.EventIndex);
    }
    private static void Sessions()
    {
        var c = new FakeClock(); var g = Engine("1", c); Check(!g.Accept(Key('Z'))); Begin(g, c); Check(g.Accept(Key('Z')));
        c.Seconds += 100; Eq(GuidePhase.Completed, g.Update().Phase); Check(!g.Accept(Key('Z')));
        g.Start(); Near(0, g.Snapshot.PositionBeats); g.Stop(); Check(!g.Accept(Key('Z')));
        var next = Engine("2 3 4", c); Eq(0, next.RangeStart); Eq(2, next.RangeEnd); Check(!next.Loop); Eq(GuidePhase.Stopped, next.Phase);
    }
    private static void LongScore()
    {
        var notes = Enumerable.Range(0, 100_000).Select(i => new ScoreEvent(i % 7 + 1, 0, false, .25, 0, 1)).ToArray();
        var score = new ParsedScore(new("Long", 120, 20, ""), notes); var c = new FakeClock();
        var g = new GuideEngine(score, clock: c); Begin(g, c); c.Seconds += 12_500; g.Update();
        Eq(GuidePhase.Completed, g.Phase); Near(25_000, g.Snapshot.PositionBeats);
        g.Stop(); g.SetMode(GuideMode.WaitForKey); Begin(g, c); c.Seconds += 1_000_000;
        Eq(0, g.Update().EventIndex); Eq(GuidePhase.WaitingForKey, g.Phase);
    }
}
