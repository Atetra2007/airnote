using System.Diagnostics;
using Harmonica.Core;

namespace Harmonica.Windows;

internal sealed class WindowsPlaybackOutput : IPlaybackOutput
{
    private readonly object gate = new();
    private readonly nint probeWindow;
    private WaveTone? tone;
    private GameInputSession? input;
    private PlaybackMode mode;

    public WindowsPlaybackOutput(nint probeWindow = default) => this.probeWindow = probeWindow;

    public async Task BeginAsync(PlaybackMode requestedMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        mode = requestedMode;
        if (mode == PlaybackMode.Preview) lock (gate) tone = new WaveTone();
        if (mode == PlaybackMode.Game)
        {
            input = new(new WindowsInputBackend(), probeWindow);
            var wait = Stopwatch.StartNew();
            while (input.StartupKeysHeld && wait.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (input.StartupKeysHeld)
                throw new InvalidOperationException("启动热键或修饰键仍被按住；请松开后重新开始。");
            input.Begin();
        }
    }

    public async Task NoteOnAsync(ScoreEvent note, float volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == PlaybackMode.Preview) lock (gate) tone!.Play(GameKeyMapping.Frequency(note), volume);
        if (mode == PlaybackMode.Game) await input!.NoteOnAsync(note, cancellationToken).ConfigureAwait(false);
    }

    public async Task SilenceAsync(CancellationToken cancellationToken)
    {
        if (tone is not null) lock (gate) tone.Stop();
        if (input is not null) await input.ReleaseAsync().ConfigureAwait(false);
    }

    public void CheckSafety() { if (mode == PlaybackMode.Game) input?.CheckSafety(); }
    public void SetVolume(float volume) { lock (gate) tone?.SetVolume(volume); }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        List<Exception> errors = [];
        try { if (input is not null) await input.ReleaseAsync().ConfigureAwait(false); }
        catch (Exception ex) { errors.Add(ex); }
        try
        {
            lock (gate)
            {
                if (tone is not null) { tone.Dispose(); tone = null; }
            }
        }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("播放资源释放失败。", errors);
    }

    public void Dispose() => ReleaseAsync(CancellationToken.None).GetAwaiter().GetResult();
}
