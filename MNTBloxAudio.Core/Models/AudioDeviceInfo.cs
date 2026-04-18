namespace MNTBloxAudio.Core.Models;

public sealed class AudioDeviceInfo
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Flow { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
}
