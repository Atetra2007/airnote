using System.Runtime.InteropServices;

namespace Harmonica.Windows;

/// <summary>Small looped PCM buffers, generated locally. No audio assets, models or network.</summary>
internal sealed class WaveTone : IDisposable
{
    private nint device;
    private nint header;
    private nint samples;
    private bool prepared;
    private const int SampleRate = 44100;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WAVEHDR>();

    public WaveTone()
    {
        WAVEFORMATEX format = new()
        {
            wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate,
            nAvgBytesPerSec = SampleRate * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0
        };
        Check(waveOutOpen(out device, uint.MaxValue, ref format, 0, 0, 0),
            "无法打开音频输出。请检查 Windows 输出设备，或使用日志模式");
    }

    public void Play(double frequency, float volume)
    {
        Stop();
        if (!double.IsFinite(frequency) || frequency < 20 || frequency > 15000)
            throw new InvalidOperationException("试听音高超出支持范围。");
        // Exactly 64 complete cycles yield a seamless loop and minimal tuning error.
        int count = Math.Clamp((int)Math.Round(SampleRate * 64 / frequency), 256, 150000);
        var pcm = new short[count];
        for (var i = 0; i < count; i++)
        {
            double phase = 2 * Math.PI * 64 * i / count;
            double value = (Math.Sin(phase) + .18 * Math.Sin(phase * 2) + .07 * Math.Sin(phase * 3)) / 1.25;
            pcm[i] = (short)(short.MaxValue * .32 * value);
        }
        samples = Marshal.AllocHGlobal(pcm.Length * 2);
        Marshal.Copy(pcm, 0, samples, pcm.Length);
        header = Marshal.AllocHGlobal((int)HeaderSize);
        var data = new WAVEHDR
        {
            lpData = samples, dwBufferLength = (uint)(pcm.Length * 2), dwFlags = 4 | 8, dwLoops = uint.MaxValue
        };
        Marshal.StructureToPtr(data, header, false);
        try
        {
            Check(waveOutPrepareHeader(device, header, HeaderSize), "准备试听缓冲区失败");
            prepared = true;
            SetVolume(volume);
            Check(waveOutWrite(device, header, HeaderSize), "播放试听音频失败");
        }
        catch { Stop(); throw; }
    }

    public void SetVolume(float volume)
    {
        if (device == 0) return;
        uint value = (uint)Math.Round(Math.Clamp(volume, 0, 1) * 65535);
        Check(waveOutSetVolume(device, value | value << 16), "调整试听音量失败");
    }

    public void Stop()
    {
        if (device != 0 && header != 0 && prepared)
        {
            Check(waveOutReset(device), "停止试听音频失败");
            uint result = waveOutUnprepareHeader(device, header, HeaderSize);
            for (var retry = 0; result == 33 && retry < 10; retry++)
            {
                Thread.Sleep(5);
                result = waveOutUnprepareHeader(device, header, HeaderSize);
            }
            // Never free a buffer while the device may still own it; later Stop/Dispose can retry.
            Check(result, "释放试听缓冲区失败");
            prepared = false;
        }
        if (header != 0) { Marshal.FreeHGlobal(header); header = 0; }
        if (samples != 0) { Marshal.FreeHGlobal(samples); samples = 0; }
    }

    public void Dispose()
    {
        Stop();
        if (device == 0) return;
        Check(waveOutClose(device), "关闭试听音频设备失败");
        device = 0;
    }

    private static void Check(uint result, string message)
    {
        if (result != 0) throw new InvalidOperationException($"{message}（WinMM 错误 {result}）。");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WAVEHDR
    {
        public nint lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public nuint dwUser;
        public uint dwFlags, dwLoops;
        public nint lpNext;
        public nuint reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint waveOut, uint device, ref WAVEFORMATEX format, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint waveOut, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint waveOut, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint waveOut, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(nint waveOut);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(nint waveOut);
    [DllImport("winmm.dll")] private static extern uint waveOutSetVolume(nint waveOut, uint volume);
}
