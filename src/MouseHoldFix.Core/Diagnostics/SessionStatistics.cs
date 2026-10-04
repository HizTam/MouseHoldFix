namespace MouseHoldFix.Diagnostics;

public readonly record struct SessionStatistics(
    long TotalLeftDown, long TotalLeftUp, long DragStarts,
    long ReleaseCandidates, long RepairedReleases, long SyntheticReleases,
    long SendInputFailures, long? LastUpDownGapMs);
