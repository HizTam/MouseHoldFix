namespace MouseHoldFix.Input;

public readonly record struct NativeSendResult(int Sent, int Error);
public readonly record struct BatchDeliveryResult(int Sent, int Failures, int LastError);

public static class InputBatchDelivery
{
    /// <summary>Send a batch with at most two retries, retrying only its unsent suffix.</summary>
    public static BatchDeliveryResult Send(int count, Func<int, int, NativeSendResult> sender)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var offset = 0;
        var failures = 0;
        var error = 0;
        for (var attempt = 0; attempt < 3 && offset < count; attempt++)
        {
            var result = sender(offset, count - offset);
            if (result.Sent < 0 || result.Sent > count - offset)
                throw new InvalidOperationException("The native sender returned an invalid event count.");
            if (result.Sent < count - offset)
            {
                error = result.Error;
                failures++;
            }
            offset += result.Sent;
        }
        return new(offset, failures, error);
    }
}
