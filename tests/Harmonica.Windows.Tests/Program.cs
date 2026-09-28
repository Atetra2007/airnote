using Harmonica.Core;
using Harmonica.Windows;

// Intentionally uses only fake output/backends: CI never emits system input or opens audio.
var tests = new (string Name, Func<Task> Run)[]
{
    ("C4, octave, sharp and rest frequencies", Frequency),
    ("Game key mapping and range rejection", Mapping),
    ("Absolute timeline does not accumulate output overhead", AbsoluteTimeline),
    ("Rest and gap follow the shared timeline", RestAndGap),
    ("Game articulation separates notes without changing BPM or rests", GameArticulation),
    ("Delayed releases retain gaps and full rests instead of catch-up", LateGameRelease),
    ("Unplayable short game notes fail before emitting input", ShortGameNotes),
    ("Read-only game inspection lists every blocked note with event positions", GameInspection),
    ("Mouse pitch modifiers settle before the note key", ModifierSettling),
    ("Modifier setup fits within beats without accumulating tempo drift", ModifierTimeline),
    ("Cancel or focus change during modifier setup sends no note key", ModifierSetupCancel),
    ("Cancellation during countdown emits no note", CountdownCancel),
    ("Game countdown precedes the absolute note timeline", CountdownComplete),
    ("Long-note cancellation releases output", LongNoteCancel),
    ("Duplicate start rejected; Stop waits for release", DuplicateStartAndStop),
    ("Note output failure always releases", OutputFailure),
    ("Foreground safety failure always releases", SafetyFailure),
    ("Cleanup failure blocks restart and supports retry", CleanupRetry),
    ("Observer failure cannot prevent cleanup", ObserverFailure),
    ("Input release covers uncertain key-down and retries", InputRelease),
    ("Input rejects other foreground processes and window changes", InputGuard),
    ("Input detects external modifiers but preserves unrelated keys", ModifierGuard),
    ("Input refuses mouse actions outside the target window", MouseGuard),
    ("Hotkey parsing rejects unsafe or malformed chords", Hotkeys),
    ("Invalid volume rejected", VolumeValidation),
    ("Guide IL has no route to synthetic input/audio output", GuideIsolation.Verify),
    ("Guide Raw Input ABI and packet decoding", GuideRawPacketTests.Verify),
}.Concat(GuideTests.All).ToArray();
var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed. No real keyboard, mouse or audio output.");
return failures == 0 ? 0 : 1;

static Task Frequency()
{
    Near(261.625565, GameKeyMapping.Frequency(Note(1)), .0001);
    Near(130.812783, GameKeyMapping.Frequency(Note(1, -1)), .0001);
    Near(523.251131, GameKeyMapping.Frequency(Note(1, 1)), .0001);
    Near(1046.502261, GameKeyMapping.Frequency(Note(1, 2)), .0001);
    Near(440, GameKeyMapping.Frequency(Note(6)), .0001);
    Near(277.182631, GameKeyMapping.Frequency(Note(1, sharp: true)), .0001);
    Equal(0d, GameKeyMapping.Frequency(Note(0)));
    return Task.CompletedTask;
}

static Task Mapping()
{
    Equal("Z", string.Join("+", GameKeyMapping.ForNote(Note(1))));
    Equal("M", string.Join("+", GameKeyMapping.ForNote(Note(7))));
    Equal("鼠标左键+Z", string.Join("+", GameKeyMapping.ForNote(Note(1, -1))));
    Equal(",", string.Join("+", GameKeyMapping.ForNote(Note(1, 1))));
    Equal("鼠标右键+X", string.Join("+", GameKeyMapping.ForNote(Note(2, 1))));
    Equal("鼠标右键+,", string.Join("+", GameKeyMapping.ForNote(Note(1, 2))));
    Equal("鼠标左键+鼠标中键+C", string.Join("+", GameKeyMapping.ForNote(Note(3, -1, true))));
    Equal(0, GameKeyMapping.ForNote(Note(0)).Count);
    Throws<InvalidOperationException>(() => GameKeyMapping.ForNote(Note(2, 2)));
    return Task.CompletedTask;
}

static async Task AbsoluteTimeline()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock) { Overhead = .007 };
    await using var engine = new PlaybackEngine(() => output, clock);
    await engine.PlayAsync(Parse("1 2 3 4", 120), PlaybackMode.Log);
    SequenceNear([0, .5, 1, 1.5], output.Notes.Select(e => e.Time).ToArray());
    Near(2, clock.Seconds, .00001);
    Equal(1, output.ReleaseCount);
    True(output.Disposed);
}

static async Task RestAndGap()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock);
    await using var engine = new PlaybackEngine(() => output, clock);
    await engine.PlayAsync(Parse("1 0 2_ 3_", 120, 20), PlaybackMode.Preview);
    SequenceNear([0, 1, 1.25], output.Notes.Select(e => e.Time).ToArray());
    SequenceNear([.48, 1.23, 1.48], output.Silences.ToArray());
    Near(1.5, clock.Seconds, .00001);
}

static async Task CountdownCancel()
{
    using var cancellation = new CancellationTokenSource();
    var clock = new FakeClock();
    clock.AfterDelay = () => { if (clock.Seconds >= .25) cancellation.Cancel(); };
    var output = new FakeOutput(clock);
    await using var engine = new PlaybackEngine(() => output, clock);
    var states = new List<string>();
    engine.Progress += p => states.Add(p.State);
    await engine.PlayAsync(Parse("1"), PlaybackMode.Game, cancellation.Token);
    Equal(0, output.BeginCount);
    Equal(0, output.Notes.Count);
    Equal("Countdown", states.First());
    Equal("Stopped", states.Last());
    True(output.Disposed);
}

static async Task GameArticulation()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock);
    await using var engine = new PlaybackEngine(() => output, clock);
    await engine.PlayAsync(Parse("1 1 0:0.25 2_", 120, 10), PlaybackMode.Game);
    SequenceNear([3, 3.5, 4.125], output.Notes.Select(n => n.Time).ToArray());
    SequenceNear([3.44, 3.94, 4.315], output.Silences.ToArray());
    Near(4.375, clock.Seconds, .00001);
    Near(.025, PlaybackEngine.NoteGap(.1, 10, 60), .00001);
    Near(.08, PlaybackEngine.NoteGap(.5, 80, 60), .00001);
    Near(.01, PlaybackEngine.NoteGap(.5, 10, 0), .00001);
}

static async Task LateGameRelease()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock);
    var delayed = false;
    clock.AfterDelay = () => { if (!delayed && clock.Seconds >= 3.43) { delayed = true; clock.Seconds += .08; } };
    await using var engine = new PlaybackEngine(() => output, clock);
    await engine.PlayAsync(Parse("1 1 0:0.25 2", 120, 10), PlaybackMode.Game);
    True(output.Notes[1].Time - output.Silences[0] >= .06 - .00001, "Late release must keep a real 60ms gap");
    True(output.Notes[2].Time - output.Silences[1] >= .185 - .00001, "Written rest must remain in addition to note gap");
    True(output.Silences[1] - output.Notes[1].Time >= .44 - .00001, "Following note must not be rushed");
}

static async Task ShortGameNotes()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock);
    await using var engine = new PlaybackEngine(() => output, clock);
    await ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(Parse("1:0.010417", 60, 10), PlaybackMode.Game));
    await ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(Parse("（1）:0.03", 60, 10), PlaybackMode.Game));
    Equal(0, output.BeginCount);
    Equal(0, output.Notes.Count);
    // A score can still be inspected locally; no pitch, BPM or note is silently changed.
    await engine.PlayAsync(Parse("1:0.010417", 60, 10), PlaybackMode.Log);
    Equal(1, output.Notes.Count);
}

static async Task GameInspection()
{
    // Positions include rests, just as the editor and playback progress do.
    var score = Parse("0:0.02 1:0.02 （1）:0.04 2:0.1", 60, 10);
    var issues = PlaybackEngine.InspectGameNotes(score);
    Equal(2, issues.Count);
    Equal(2, issues[0].EventNumber); Near(10, issues[0].HoldMs, .00001);
    Equal(3, issues[1].EventNumber); Near(24, issues[1].PreparationMs, .00001);
    Near(6, issues[1].HoldMs, .00001);
    Equal("0:0.02 1:0.02 （1）:0.04 2:0.1", score.Document.Body);
    Equal(0, PlaybackEngine.InspectGameNotes(Parse("1 （1） 0", 60, 10)).Count);
    // A larger requested gap can change eligibility. No playback state is involved.
    var borderline = Parse("（1）:0.047", 60, 10);
    Equal(0, PlaybackEngine.InspectGameNotes(borderline, 0).Count);
    Equal(1, PlaybackEngine.InspectGameNotes(borderline, 60).Count);
    Throws<ArgumentOutOfRangeException>(() => PlaybackEngine.InspectGameNotes(score, 251));
    var clock = new FakeClock();
    var created = 0;
    await using var engine = new PlaybackEngine(() => { created++; return new FakeOutput(clock); }, clock);
    try { await engine.PlayAsync(score, PlaybackMode.Game); throw new Exception("Expected timing rejection"); }
    catch (InvalidOperationException ex) { Equal(issues[0].Message, ex.Message); }
    Equal(0, created); Near(0, clock.Seconds, .00001);
}

static async Task ModifierSettling()
{
    var clock = new FakeClock();
    var backend = new FakeInputBackend();
    var session = new GameInputSession(backend, clock: clock);
    session.Begin();
    clock.AfterDelay = () => True(!backend.Down.Contains((ushort)'C'), "Note key must wait for mouse modifiers");
    await session.NoteOnAsync(Note(3, -1, true));
    Near(.024, clock.Seconds, .00001);
    True(backend.Down.SetEquals([1, 4, (ushort)'C']));
    await session.ReleaseAsync();
    Equal(0, backend.Down.Count);
    clock.AfterDelay = null;
    await session.NoteOnAsync(Note(1));
    Near(.024, clock.Seconds, .00001);
    await session.ReleaseAsync();
}

static async Task ModifierSetupCancel()
{
    foreach (var cancel in new[] { true, false })
    {
        var clock = new FakeClock();
        var backend = new FakeInputBackend();
        using var cancellation = new CancellationTokenSource();
        var session = new GameInputSession(backend, clock: clock);
        session.Begin();
        clock.AfterDelay = () => { if (cancel) cancellation.Cancel(); else backend.ForegroundWindow = 9; };
        if (cancel) await ThrowsAsync<OperationCanceledException>(() => session.NoteOnAsync(Note(2, 1), cancellation.Token));
        else await ThrowsAsync<InvalidOperationException>(() => session.NoteOnAsync(Note(2, 1)));
        True(!backend.Events.Any(e => e == "X:down"));
        await session.ReleaseAsync();
        Equal(0, backend.Down.Count);
    }
}

static async Task ModifierTimeline()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock) { SimulateModifierSetup = true };
    await using var engine = new PlaybackEngine(() => output, clock);
    await engine.PlayAsync(Parse("（1） 2 【3】 0 4", 120, 10), PlaybackMode.Game);
    SequenceNear([3.024, 3.5, 4.024, 5], output.Notes.Select(n => n.Time).ToArray());
    SequenceNear([3.44, 3.94, 4.44, 5.44], output.Silences.ToArray());
    Near(5.5, clock.Seconds, .00001);
}

static async Task LongNoteCancel()
{
    using var cancellation = new CancellationTokenSource();
    var clock = new FakeClock();
    clock.AfterDelay = () => { if (clock.Seconds >= .12) cancellation.Cancel(); };
    var output = new FakeOutput(clock);
    await using var engine = new PlaybackEngine(() => output, clock);
    await engine.PlayAsync(Parse("1:32 2", 60), PlaybackMode.Preview, cancellation.Token);
    Equal(1, output.Notes.Count);
    True(clock.Seconds < .14);
    Equal(1, output.ReleaseCount);
    True(output.Disposed);
}

static async Task CountdownComplete()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock);
    await using var engine = new PlaybackEngine(() => output, clock);
    var states = new List<string>();
    engine.Progress += progress => states.Add(progress.State);
    await engine.PlayAsync(Parse("1 2"), PlaybackMode.Game);
    Equal(3, states.Count(state => state == "Countdown"));
    SequenceNear([3, 3.5], output.Notes.Select(note => note.Time).ToArray());
    Equal("Completed", states.Last());
    Near(4, clock.Seconds, .00001);
}

static async Task DuplicateStartAndStop()
{
    var output = new FakeOutput(new FakeClock());
    var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    output.OnNote = () => began.TrySetResult();
    await using var engine = new PlaybackEngine(() => output, new MonotonicPlaybackClock());
    var play = engine.PlayAsync(Parse("1:32", 60), PlaybackMode.Preview);
    await began.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Throws<InvalidOperationException>(() => engine.PlayAsync(Parse("2"), PlaybackMode.Preview));
    await engine.StopAsync();
    await play;
    True(!engine.IsPlaying);
    True(output.Disposed);
}

static async Task OutputFailure()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock) { FailOnNote = true };
    var engine = new PlaybackEngine(() => output, clock);
    await ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(Parse("1"), PlaybackMode.Preview));
    Equal(1, output.ReleaseCount);
    True(output.Disposed);
    try { await engine.DisposeAsync(); } catch (InvalidOperationException) { }
}

static async Task SafetyFailure()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock) { FailSafetyAt = .15 };
    var engine = new PlaybackEngine(() => output, clock);
    await ThrowsAsync<InvalidOperationException>(() => engine.PlayAsync(Parse("1:32", 60), PlaybackMode.Preview));
    Equal(1, output.Notes.Count);
    True(output.Disposed);
    try { await engine.DisposeAsync(); } catch (InvalidOperationException) { }
}

static async Task CleanupRetry()
{
    var clock = new FakeClock();
    var output = new FakeOutput(clock) { FailRelease = true };
    var engine = new PlaybackEngine(() => output, clock);
    await ThrowsAsync<AggregateException>(() => engine.PlayAsync(Parse("1"), PlaybackMode.Log));
    True(engine.NeedsCleanup);
    Throws<InvalidOperationException>(() => engine.PlayAsync(Parse("1"), PlaybackMode.Log));
    output.FailRelease = false;
    try { await engine.StopAsync(); } catch (AggregateException) { }
    True(!engine.NeedsCleanup);
    await engine.PlayAsync(Parse("1"), PlaybackMode.Log);
    await engine.DisposeAsync();
    True(output.Disposed);
}

static async Task ObserverFailure()
{
    var output = new FakeOutput(new FakeClock());
    await using var engine = new PlaybackEngine(() => output, new FakeClock());
    engine.Progress += _ => throw new InvalidOperationException("closed UI");
    engine.Diagnostic += _ => throw new InvalidOperationException("closed logger");
    await engine.PlayAsync(Parse("1"), PlaybackMode.Log);
    True(output.Disposed);
}

static async Task InputRelease()
{
    var backend = new FakeInputBackend { FailDown = (ushort)'Z', UpFailures = 1 };
    var session = new GameInputSession(backend);
    session.Begin();
    await ThrowsAsync<InvalidOperationException>(() => session.NoteOnAsync(Note(1, -1)));
    await session.ReleaseAsync();
    True(backend.Events.Any(e => e == "Z:up"));
    True(backend.Events.Any(e => e == "鼠标左键:up"));
    Equal(0, backend.Down.Count);
    True(!backend.Events.Any(e => e.Contains("X")));
}

static async Task InputGuard()
{
    var backend = new FakeInputBackend { Name = "notepad" };
    var session = new GameInputSession(backend);
    Throws<InvalidOperationException>(session.Begin);
    Equal(0, backend.Events.Count);
    backend.Name = "DeltaForceClient-Win64-Shipping";
    session.Begin();
    await session.NoteOnAsync(Note(2, 1));
    backend.ForegroundWindow = 9;
    Throws<InvalidOperationException>(session.CheckSafety);
    await session.ReleaseAsync();
    Equal(0, backend.Down.Count);
}

static async Task ModifierGuard()
{
    var backend = new FakeInputBackend();
    backend.Down.Add(0x11);
    var session = new GameInputSession(backend);
    Throws<InvalidOperationException>(session.Begin);
    await session.ReleaseAsync();
    True(backend.Down.Contains(0x11));
    Equal(0, backend.Events.Count);
    backend.Down.Clear();
    session.Begin();
    await session.NoteOnAsync(Note(1));
    backend.Down.Add(0x12);
    Throws<InvalidOperationException>(session.CheckSafety);
    await session.ReleaseAsync();
    True(backend.Down.SetEquals([0x12]));
}

static async Task MouseGuard()
{
    var backend = new FakeInputBackend { CursorRootWindow = 99 };
    var session = new GameInputSession(backend);
    session.Begin();
    await ThrowsAsync<InvalidOperationException>(() => session.NoteOnAsync(Note(1, -1)));
    Equal(0, backend.Events.Count);
    backend.CursorRootWindow = 1;
    await session.NoteOnAsync(Note(1, -1));
    backend.CursorRootWindow = 99;
    Throws<InvalidOperationException>(session.CheckSafety);
    await session.ReleaseAsync();
    Equal(0, backend.Down.Count);
}

static Task Hotkeys()
{
    True(HotkeyChord.TryParse("Ctrl+Shift+F8", out var chord, out _));
    Equal(6u, chord!.Modifiers);
    Equal(0x77u, chord.VirtualKey);
    foreach (var value in new[] { "Z", "Alt+Z", "Win+F6", "F13", "F0", "Ctrl+Ctrl+F6", "F6+F8", "+F6", "", "F01", "F 1" })
        True(!HotkeyChord.TryParse(value, out _, out _), value);
    return Task.CompletedTask;
}

static Task VolumeValidation()
{
    using var engine = new PlaybackEngine(() => new FakeOutput(new FakeClock()));
    Equal(.2f, engine.Volume);
    foreach (var value in new[] { -.01f, 1.1f, float.NaN, float.PositiveInfinity })
        Throws<ArgumentOutOfRangeException>(() => engine.Volume = value);
    engine.Volume = 0;
    return Task.CompletedTask;
}

static ScoreEvent Note(int degree, int octave = 0, bool sharp = false) => new(degree, octave, sharp, 1, 0, 1);
static ParsedScore Parse(string body, int bpm = 120, int gap = 20) => ScoreCodec.Parse(new("Test", bpm, gap, body));
static void True(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Near(double expected, double actual, double tolerance) { if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected}, got {actual}"); }
static void SequenceNear(double[] expected, double[] actual)
{
    Equal(expected.Length, actual.Length);
    for (var i = 0; i < expected.Length; i++) Near(expected[i], actual[i], .00001);
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

sealed class FakeClock : IPlaybackClock
{
    public double Seconds { get; set; }
    public Action? AfterDelay { get; set; }
    public Task DelayAsync(TimeSpan delay, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Seconds += Math.Max(.0000001, delay.TotalSeconds);
        AfterDelay?.Invoke();
        return Task.CompletedTask;
    }
}

sealed class FakeOutput(FakeClock clock) : IPlaybackOutput
{
    public List<(double Time, ScoreEvent Note)> Notes { get; } = [];
    public List<double> Silences { get; } = [];
    public int BeginCount, ReleaseCount;
    public bool Disposed, FailOnNote, FailRelease;
    public bool SimulateModifierSetup;
    public double Overhead, FailSafetyAt = double.MaxValue;
    public Action? OnNote;
    public Task BeginAsync(PlaybackMode mode, CancellationToken token) { BeginCount++; return Task.CompletedTask; }
    public Task NoteOnAsync(ScoreEvent note, float volume, CancellationToken token)
    {
        if (SimulateModifierSetup && GameKeyMapping.ForNote(note).Any(k => k.Mouse)) clock.Seconds += GameInputSession.ModifierSettleSeconds;
        Notes.Add((clock.Seconds, note));
        clock.Seconds += Overhead;
        OnNote?.Invoke();
        if (FailOnNote) throw new InvalidOperationException("simulated output failure");
        return Task.CompletedTask;
    }
    public Task SilenceAsync(CancellationToken token) { Silences.Add(clock.Seconds); clock.Seconds += Overhead; return Task.CompletedTask; }
    public void CheckSafety() { if (clock.Seconds >= FailSafetyAt) throw new InvalidOperationException("simulated foreground change"); }
    public void SetVolume(float volume) { }
    public Task ReleaseAsync(CancellationToken token)
    {
        ReleaseCount++;
        if (FailRelease) throw new InvalidOperationException("simulated release failure");
        return Task.CompletedTask;
    }
    public void Dispose()
    {
        if (FailRelease) throw new InvalidOperationException("simulated final cleanup failure");
        Disposed = true;
    }
}

sealed class FakeInputBackend : IInputBackend
{
    public nint ForegroundWindow { get; set; } = 1;
    public nint CursorRootWindow { get; set; } = 1;
    public string Name = "DeltaForceClient-Win64-Shipping";
    public uint WindowProcess(nint window) => window == 0 ? 0u : 42u;
    public string ProcessName(uint process) => Name;
    public HashSet<ushort> Down { get; } = [];
    public List<string> Events { get; } = [];
    public ushort FailDown;
    public int UpFailures;
    public bool IsDown(ushort key) => Down.Contains(key);
    public void Send(PerformanceKey key, bool down)
    {
        Events.Add($"{key}:{(down ? "down" : "up")}");
        if (down)
        {
            Down.Add(key.VirtualKey);
            if (key.VirtualKey == FailDown) throw new InvalidOperationException("uncertain down failure");
        }
        else
        {
            if (UpFailures-- > 0) throw new InvalidOperationException("temporary up failure");
            Down.Remove(key.VirtualKey);
        }
    }
}
