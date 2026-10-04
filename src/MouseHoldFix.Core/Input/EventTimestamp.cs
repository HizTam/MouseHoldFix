namespace MouseHoldFix.Input;

public static class EventTimestamp
{
    /// <summary>Map a recent native 32-bit uptime to the same epoch as the host's 64-bit uptime.</summary>
    public static long Extend(uint eventTime, long currentUptime) =>
        currentUptime - unchecked((uint)currentUptime - eventTime);
}
