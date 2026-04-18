namespace MNTBloxAudio.Core.Models;

public sealed class RobloxAssetResolutionInfo
{
    public string AssetId { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}
