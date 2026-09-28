using System.Diagnostics;
using Harmonica.Core;

namespace Harmonica.Windows;

public enum PlaybackMode { Preview, Game, Log }

public sealed record PlaybackProgress(string State, int EventIndex, int EventCount,
    double ElapsedSeconds, double TotalSeconds);

public sealed record GameNoteIssue(int EventNumber, string Label, double DurationMs,
    double GapMs, double PreparationMs, double HoldMs)
{
    public string Message => $"第 {EventNumber} 个音（{Label}，{DurationMs:0.#} ms）过短，无法留出换音与按键时间。请降低 BPM 或校对该短音；本次未发送按键。";
}

/// <summary>A monotonic clock; absolute deadlines prevent per-note work accumulating drift.</summary>
public interface IPlaybackClock
{
    double Seconds { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class MonotonicPlaybackClock : IPlaybackClock
{
    public double Seconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

/// <summary>Injected output allows verification without emitting sound or system input.</summary>
public interface IPlaybackOutput : IDisposable
{
    Task BeginAsync(PlaybackMode mode, CancellationToken cancellationToken);
    Task NoteOnAsync(ScoreEvent note, float volume, CancellationToken cancellationToken);
    Task SilenceAsync(CancellationToken cancellationToken);
    void CheckSafety();
    void SetVolume(float volume);
    Task ReleaseAsync(CancellationToken cancellationToken);
}

public sealed class PlaybackEngine : IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim stopGate = new(1, 1);
    private readonly IPlaybackClock clock;
    private readonly Func<IPlaybackOutput> outputFactory;
    private CancellationTokenSource? cancellation;
    private Task? running;
    private IPlaybackOutput? activeOutput;
    private IPlaybackOutput? pendingCleanup;
    private bool disposed;
    private float volume = .2f;

    public PlaybackEngine() : this(() => new WindowsPlaybackOutput(), new MonotonicPlaybackClock()) { }

    public PlaybackEngine(Func<IPlaybackOutput> outputFactory, IPlaybackClock? clock = null)
    {
        this.outputFactory = outputFactory ?? throw new ArgumentNullException(nameof(outputFactory));
        this.clock = clock ?? new MonotonicPlaybackClock();
    }

    // The probe may send only to a foreground window belonging to this process.
    public static PlaybackEngine CreateInputProbe(nint targetWindow) =>
        new(() => new WindowsPlaybackOutput(targetWindow), new MonotonicPlaybackClock());

    public bool IsPlaying { get { lock (gate) return running is { IsCompleted: false }; } }
    public bool NeedsCleanup { get { lock (gate) return pendingCleanup is not null; } }

    public float Volume
    {
        get { lock (gate) return volume; }
        set
        {
            if (!float.IsFinite(value) || value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value), "音量必须在 0–1 之间。");
            lock (gate)
            {
                volume = value;
                activeOutput?.SetVolume(value);
            }
        }
    }

    public event Action<PlaybackProgress>? Progress;
    public event Action<string>? Diagnostic;

    public Task PlayAsync(ParsedScore score, PlaybackMode mode, CancellationToken token = default, int gameGapMs = 60)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (gameGapMs is < 0 or > 250) throw new ArgumentOutOfRangeException(nameof(gameGapMs));
        if (score.Events.Count == 0) throw new InvalidOperationException("曲谱没有可播放的音符。");
        if (score.Document.Bpm <= 0 || score.Document.GapMs < 0 ||
            score.Events.Any(e => !double.IsFinite(e.Beats) || e.Beats <= 0))
            throw new InvalidOperationException("曲谱速度、间隔或时值无效。");
        if (mode == PlaybackMode.Game && InspectGameNotes(score, gameGapMs).FirstOrDefault() is { } issue)
            throw new InvalidOperationException(issue.Message);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (running is { IsCompleted: false })
                throw new InvalidOperationException("正在播放，请先停止当前曲目。");
            if (pendingCleanup is not null)
                throw new InvalidOperationException("上次播放仍有资源未确认释放，请先点击停止重试。");
            cancellation?.Dispose();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var sessionCancellation = cancellation;
            // IsPlaying becomes true before work or callbacks can start.
            running = Task.Run(() => RunAsync(score, mode, gameGapMs, sessionCancellation.Token), CancellationToken.None);
            return running;
        }
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (gate)
        {
            cancellation?.Cancel();
            // A completed playback error belongs to PlayAsync; don't replay it forever on close.
            task = running is { IsCompleted: false } ? running : null;
        }
        await stopGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Exception? playbackFailure = null;
            try { if (task is not null) await task.ConfigureAwait(false); }
            catch (Exception ex) { playbackFailure = ex; }
            IPlaybackOutput? cleanup;
            lock (gate) cleanup = pendingCleanup;
            if (cleanup is not null)
            {
                await cleanup.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                cleanup.Dispose();
                lock (gate) if (ReferenceEquals(cleanup, pendingCleanup)) pendingCleanup = null;
                Log("此前未完成的释放已重试成功。");
            }
            if (playbackFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(playbackFailure).Throw();
        }
        finally { stopGate.Release(); }
    }

    internal static double NoteGap(double duration, int scoreGapMs, int gameGapMs) =>
        Math.Max(scoreGapMs / 1000d, Math.Min(gameGapMs / 1000d, duration * .25));

    /// <summary>Checks timing only. Never creates an output or sends keyboard, mouse or audio.</summary>
    public static IReadOnlyList<GameNoteIssue> InspectGameNotes(ParsedScore score, int gameGapMs = 60)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (gameGapMs is < 0 or > 250) throw new ArgumentOutOfRangeException(nameof(gameGapMs));
        if (score.Events.Count == 0) throw new InvalidOperationException("曲谱没有可播放的音符。");
        if (score.Document.Bpm <= 0 || score.Document.GapMs < 0 ||
            score.Events.Any(e => !double.IsFinite(e.Beats) || e.Beats <= 0))
            throw new InvalidOperationException("曲谱速度、间隔或时值无效。");
        var issues = new List<GameNoteIssue>();
        for (var i = 0; i < score.Events.Count; i++)
        {
            var note = score.Events[i];
            if (note.Degree == 0) continue;
            var duration = note.Beats * 60d / score.Document.Bpm;
            var preparation = GameKeyMapping.ForNote(note).Any(k => k.Mouse) ? GameInputSession.ModifierSettleSeconds : 0;
            var gap = NoteGap(duration, score.Document.GapMs, gameGapMs);
            var hold = duration - gap - preparation;
            if (hold < .012)
                issues.Add(new(i + 1, note.Label, duration * 1000, gap * 1000, preparation * 1000, hold * 1000));
        }
        return issues.AsReadOnly();
    }

    private async Task RunAsync(ParsedScore score, PlaybackMode mode, int gameGapMs, CancellationToken token)
    {
        IPlaybackOutput? output = null;
        Exception? failure = null;
        bool completed = false;
        double started = clock.Seconds;
        double elapsed = 0;
        int index = -1;
        try
        {
            token.ThrowIfCancellationRequested();
            output = outputFactory();
            lock (gate) activeOutput = output;
            if (mode == PlaybackMode.Game)
            {
                var countdownStart = clock.Seconds;
                for (var seconds = 3; seconds > 0; seconds--)
                {
                    token.ThrowIfCancellationRequested();
                    Report(new("Countdown", -1, score.Events.Count, 0, score.DurationSeconds));
                    Log($"{seconds} 秒后开始；请切回游戏并松开启动热键。F8 或设置的停止热键可取消。");
                    await WaitUntilAsync(countdownStart + 4 - seconds, null, token).ConfigureAwait(false);
                }
            }
            await output.BeginAsync(mode, token).ConfigureAwait(false);
            started = clock.Seconds;
            var beatsElapsed = 0d;
            var timelineShift = 0d;
            var notBefore = started;
            for (index = 0; index < score.Events.Count; index++)
            {
                var note = score.Events[index];
                double startDeadline = started + beatsElapsed * 60d / score.Document.Bpm + timelineShift;
                if (notBefore > startDeadline)
                {
                    timelineShift += notBefore - startDeadline;
                    startDeadline = notBefore;
                }
                double duration = note.Beats * 60d / score.Document.Bpm;
                double endDeadline = startDeadline + duration;
                await WaitUntilAsync(startDeadline, output, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                output.CheckSafety();
                elapsed = Math.Max(0, clock.Seconds - started);
                Report(new("Playing", index, score.Events.Count, elapsed, score.DurationSeconds));
                if (mode == PlaybackMode.Log)
                    Log($"{index + 1}/{score.Events.Count} {note.Label} · {note.Beats:0.###} 拍 · {elapsed * 1000:0} ms");
                if (note.Degree != 0)
                {
                    // A late wake-up must not turn a note into a near-zero pulse.
                    if (mode == PlaybackMode.Game && clock.Seconds > startDeadline + .005)
                    {
                        var late = clock.Seconds - startDeadline;
                        timelineShift += late; startDeadline += late; endDeadline += late;
                    }
                    await output.NoteOnAsync(note, Volume, token).ConfigureAwait(false);
                    var gap = mode == PlaybackMode.Game ? NoteGap(duration, score.Document.GapMs, gameGapMs) : score.Document.GapMs / 1000d;
                    var hold = Math.Max(0, duration - gap);
                    var releaseAt = startDeadline + hold;
                    if (mode == PlaybackMode.Game)
                    {
                        var preparation = GameKeyMapping.ForNote(note).Any(k => k.Mouse) ? GameInputSession.ModifierSettleSeconds : 0;
                        // Preserve the sounding part even if modifier setup or scheduling runs late.
                        releaseAt = Math.Max(releaseAt, clock.Seconds + hold - preparation);
                    }
                    await WaitUntilAsync(releaseAt, output, token).ConfigureAwait(false);
                    await output.SilenceAsync(CancellationToken.None).ConfigureAwait(false);
                    notBefore = mode == PlaybackMode.Game ? clock.Seconds + gap : endDeadline;
                }
                else
                {
                    // Rest length starts when silence is actually reached, not at a missed deadline.
                    notBefore = clock.Seconds + duration;
                }
                await WaitUntilAsync(Math.Max(endDeadline, notBefore), output, token).ConfigureAwait(false);
                beatsElapsed += note.Beats;
            }
            completed = true;
            elapsed = score.DurationSeconds;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Log("播放已取消，正在释放本程序的音频与按键。");
        }
        catch (Exception ex)
        {
            failure = ex;
            Log(ex.Message);
        }
        finally
        {
            if (output is not null)
            {
                try { await output.ReleaseAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    failure = failure is null ? ex : new AggregateException(failure, ex);
                    Log("释放失败：" + ex.Message);
                }
                try { output.Dispose(); }
                catch (Exception ex)
                {
                    failure = failure is null ? ex : new AggregateException(failure, ex);
                    Log("清理失败：" + ex.Message);
                    lock (gate) pendingCleanup = output;
                }
            }
            lock (gate) activeOutput = null;
            var state = failure is not null ? "Error" : completed ? "Completed" : "Stopped";
            Report(new(state, completed ? score.Events.Count : index, score.Events.Count,
                Math.Clamp(elapsed, 0, score.DurationSeconds), score.DurationSeconds));
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task WaitUntilAsync(double deadline, IPlaybackOutput? output, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            output?.CheckSafety();
            var remaining = deadline - clock.Seconds;
            if (remaining <= 0) return;
            // Check foreground and cancellation during long notes, not only at note boundaries.
            await clock.DelayAsync(TimeSpan.FromSeconds(Math.Min(.01, remaining)), token).ConfigureAwait(false);
        }
    }

    // UI event handlers must not prevent key release if a window is already closing.
    private void Report(PlaybackProgress progress)
    {
        try { Progress?.Invoke(progress); } catch (Exception ex) { Log("界面进度回调失败：" + ex.Message); }
    }
    private void Log(string message) { try { Diagnostic?.Invoke(message); } catch { } }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        try { StopAsync().GetAwaiter().GetResult(); }
        finally { lock (gate) { cancellation?.Dispose(); cancellation = null; } }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        try { await StopAsync().ConfigureAwait(false); }
        finally { lock (gate) { cancellation?.Dispose(); cancellation = null; } }
    }
}
