using System.Runtime.InteropServices;
using Harmonica.Windows;

internal static class GuideRawPacketTests
{
    public static Task Verify()
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Check(Marshal.SizeOf<GuideNative.RawDevice>() == 8 + IntPtr.Size, "RAWINPUTDEVICE ABI");
        Check(Marshal.SizeOf<GuideNative.RawHeader>() == 8 + 2 * IntPtr.Size, "RAWINPUTHEADER ABI");
        Check(Marshal.SizeOf<GuideNative.RawMouse>() == 24, "RAWMOUSE ABI");
        Check(Marshal.SizeOf<GuideNative.RawKeyboard>() == 16, "RAWKEYBOARD ABI");
        Check(Marshal.OffsetOf<GuideNative.RawMouse>(nameof(GuideNative.RawMouse.ButtonFlags)).ToInt32() == 4, "Mouse flags union offset");
        Check(Marshal.OffsetOf<GuideNative.RawInput>(nameof(GuideNative.RawInput.Data)).ToInt32() == Marshal.SizeOf<GuideNative.RawHeader>(), "RAWINPUT union alignment");
        var tracker = new PhysicalNoteTracker();
        GuideKeyPress? Mouse(ushort flags) => RawNoteInput.Decode(new() { Header = new() { Type = 0 }, Data = new() { Mouse = new() { ButtonFlags = flags } } }, tracker);
        GuideKeyPress? Keyboard(ushort key, ushort flags = 0) => RawNoteInput.Decode(new() { Header = new() { Type = 1 }, Data = new() { Keyboard = new() { VKey = key, Flags = flags } } }, tracker);
        Check(Mouse(1 | 16) == null, "Mouse-only packet must not count as a note");
        Check(Keyboard(67) == new GuideKeyPress(67, NoteModifiers.Left | NoteModifiers.Middle), "Combined mouse + C decode");
        Check(Keyboard(67) == null, "Raw autorepeat must be ignored");
        Mouse(0); // Movement packet must not clear modifiers.
        Keyboard(67, 1); Check(Keyboard(67) == new GuideKeyPress(67, NoteModifiers.Left | NoteModifiers.Middle), "Release/repress with held mouse");
        Mouse(2 | 32 | 4); Keyboard(67, 1);
        Check(Keyboard(188) == new GuideKeyPress(188, NoteModifiers.Right), "Mouse transitions and comma key");
        Check(Keyboard(65) == null && Keyboard(255) == null, "Unrelated/invalid keys must not be surfaced");
        Mouse(4 | 8); Check(tracker.Modifiers == NoteModifiers.None, "Coalesced down/up cannot leave a held mouse");
        tracker.Reset([188], NoteModifiers.Right); Check(Keyboard(188) == null, "Seeded key repeat cannot pass");
        Keyboard(188, 1); Check(Keyboard(188) != null, "Seeded key works after physical up");
        Check(RawNoteInput.Decode(new() { Header = new() { Type = 2 } }, tracker) == null, "Ignore HID packets");
        return Task.CompletedTask;
    }
}
