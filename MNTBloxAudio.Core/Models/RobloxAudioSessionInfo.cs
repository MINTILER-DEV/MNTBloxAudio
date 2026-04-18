namespace MNTBloxAudio.Core.Models;

public sealed class RobloxAudioSessionInfo
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public string DeviceId { get; init; } = string.Empty;

    public float Volume { get; init; }

    public float PeakMeter { get; init; }

    public bool IsMuted { get; init; }

    public bool HasAudibleActivity { get; init; }

    public string State { get; init; } = string.Empty;

    public string SessionIdentifier { get; init; } = string.Empty;
}
