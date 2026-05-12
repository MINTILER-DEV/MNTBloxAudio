namespace MNTBloxAudio.Core.Models;

public sealed class SongIndexEntry
{
    public string Code { get; init; } = string.Empty;

    public string LinkedAssetId { get; init; } = string.Empty;

    public string SongName { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string UploaderName { get; init; } = string.Empty;

    public string UploadedByDeviceId { get; init; } = string.Empty;

    public string AudioUrl { get; init; } = string.Empty;

    public DateTimeOffset? UploadedAt { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string LinkedAssetIdDisplay => string.IsNullOrWhiteSpace(LinkedAssetId)
        ? "No linked Roblox ID"
        : $"Linked Roblox ID {LinkedAssetId}";
}
