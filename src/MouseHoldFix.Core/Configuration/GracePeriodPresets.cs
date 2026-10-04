namespace MouseHoldFix.Configuration;

public static class GracePeriodPresets
{
    public static IReadOnlyList<int> Values { get; } = Array.AsReadOnly<int>(
        [5, 10, 15, 20, 30, 50, 75, 100, 125, 150, 200]);
}
