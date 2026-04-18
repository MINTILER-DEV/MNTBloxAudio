namespace MNTBloxAudio.Core.Models;

public sealed class DownloadedAssetInfo
{
    public string AssetId { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long Length { get; init; }
}
