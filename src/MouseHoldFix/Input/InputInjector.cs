using System.Runtime.InteropServices;

namespace MouseHoldFix.Input;

internal readonly record struct InjectionResult(bool Success, bool ReleaseSent, int Failures, int LastError);

internal static class InputInjector
{
    internal static InjectionResult Release(bool replayDown = false)
    {
        NativeMethods.Input Create(uint flags) => new()
        {
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseNativeInput { Flags = flags, ExtraInfo = NativeMethods.Marker }
            }
        };

        var inputs = replayDown
            ? new[] { Create(NativeMethods.LeftUp), Create(NativeMethods.LeftDown) }
            : new[] { Create(NativeMethods.LeftUp) };
        // Retry only the UNSENT suffix. Repeating a partially inserted Up/Down batch
        // would create extra clicks. No sleep and no unbounded retries.
        var delivery = InputBatchDelivery.Send(inputs.Length, (offset, count) =>
        {
            var remaining = offset == 0 ? inputs : inputs[offset..];
            var sent = (int)NativeMethods.SendInput((uint)count, remaining, Marshal.SizeOf<NativeMethods.Input>());
            return new NativeSendResult(sent, sent < count ? Marshal.GetLastPInvokeError() : 0);
        });
        return new(delivery.Sent == inputs.Length, delivery.Sent >= 1, delivery.Failures, delivery.LastError);
    }
}
