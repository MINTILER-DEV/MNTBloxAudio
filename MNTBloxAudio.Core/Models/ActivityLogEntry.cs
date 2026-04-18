namespace MNTBloxAudio.Core.Models;

public sealed class ActivityLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Category { get; init; } = "General";

    public string Message { get; init; } = string.Empty;
}
