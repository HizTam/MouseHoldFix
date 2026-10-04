namespace MouseHoldFix.Configuration;

public sealed record AppSettings
{
    public const int MinimumGraceMs = 1;
    public const int MaximumGraceMs = 200;
    public const int DefaultGraceMs = 15;

    public bool Enabled { get; init; } = true;
    public int ReleaseGraceMs { get; init; } = DefaultGraceMs;
    public bool DragOnly { get; init; } = true;
    public bool DiagnosticLogging { get; init; }
    public bool StartWithWindows { get; init; }

    public AppSettings Normalize() => this with
    {
        ReleaseGraceMs = ReleaseGraceMs is >= MinimumGraceMs and <= MaximumGraceMs ? ReleaseGraceMs : DefaultGraceMs
    };
}
