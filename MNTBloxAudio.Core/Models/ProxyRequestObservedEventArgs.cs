namespace MNTBloxAudio.Core.Models;

public sealed class ProxyRequestObservedEventArgs : EventArgs
{
    public string Host { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public bool IsAssetDeliveryRequest { get; init; }

    public string? AssetId { get; init; }

    public string? MatchedRuleName { get; init; }

    public bool ResponseWasReplaced { get; init; }
}
