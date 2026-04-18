namespace MNTBloxAudio.Core.Models;

public sealed class RobloxSoundCacheEntry
{
    public string FileName { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public long Length { get; init; }

    public DateTimeOffset LastWriteTime { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public string MatchedAssetId { get; init; } = string.Empty;

    public string MatchedRuleName { get; init; } = string.Empty;

    public long MatchedSourceAssetLength { get; init; }

    public long MatchedReplacementFileLength { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public string LengthDisplay => FormatKilobytes(Length);

    public string MatchedSourceAssetLengthDisplay => FormatKilobytes(MatchedSourceAssetLength);

    public string MatchedReplacementFileLengthDisplay => FormatKilobytes(MatchedReplacementFileLength);

    private static string FormatKilobytes(long bytes) => bytes <= 0 ? "-" : $"{bytes / 1024d:N1} KB";
}
