namespace MNTBloxAudio.Core.Models;

public sealed class ResolvedReplacementSource
{
    public string Source { get; init; } = string.Empty;

    public string LocalPath { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public long Length { get; init; }

    public bool IsRemote { get; init; }
}
