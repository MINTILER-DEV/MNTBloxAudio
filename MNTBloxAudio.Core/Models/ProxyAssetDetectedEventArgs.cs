namespace MNTBloxAudio.Core.Models;

public sealed class ProxyAssetDetectedEventArgs : EventArgs
{
    public string AssetId { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;

    public string? MatchedRuleName { get; init; }

    public bool ResponseWasReplaced { get; init; }
}
