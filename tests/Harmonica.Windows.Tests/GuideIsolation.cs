using System.Reflection;
using System.Reflection.Emit;
using Harmonica.Windows;

internal static class GuideIsolation
{
    // Walk real IL, including generated closures, so the read-only transport cannot acquire a
    // transitive route to auto-playback output through an innocent-looking helper in this assembly.
    public static Task Verify()
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        var assembly = typeof(GuideEngine).Assembly;
        var queue = new Queue<MethodBase>(); var visited = new HashSet<MethodBase>();
        foreach (var type in new[] { typeof(GuideEngine), typeof(RawNoteInput), typeof(GuideTarget), typeof(PhysicalNoteTracker) })
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Cast<MethodBase>().Concat(type.GetConstructors())) queue.Enqueue(method);
        while (queue.TryDequeue(out var method))
        {
            if (!visited.Add(method)) continue;
            if (method.DeclaringType?.Assembly != assembly) continue;
            var typeName = method.DeclaringType?.Name ?? "";
            if (method.Name == "SendInput" || typeName is "PlaybackEngine" or "GameInputSession" or "IInputBackend" or "Win32InputBackend" or "IPlaybackOutput")
                throw new Exception($"Read-only guide can reach synthetic/audio output: {method.DeclaringType}.{method.Name}");
            var bytes = method.GetMethodBody()?.GetILAsByteArray(); if (bytes == null) continue;
            for (int at = 0; at < bytes.Length;)
            {
                ushort code = bytes[at++]; if (code == 0xfe) code = (ushort)(0xfe00 | bytes[at++]);
                var op = opcodes[code];
                if (op.OperandType == OperandType.InlineMethod)
                {
                    var called = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, at));
                    if (called != null) queue.Enqueue(called);
                }
                at += op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, at),
                    _ => 4
                };
            }
        }
        return Task.CompletedTask;
    }
}
