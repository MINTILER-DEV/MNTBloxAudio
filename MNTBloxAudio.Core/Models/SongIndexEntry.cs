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

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPlayableAudio => Uri.TryCreate(AudioUrl, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusDisplay => !HasPlayableAudio
        ? "Missing Audio"
        : string.IsNullOrWhiteSpace(LinkedAssetId)
            ? "Direct Only"
            : "Linked";

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusTone => !HasPlayableAudio
        ? "Warn"
        : string.IsNullOrWhiteSpace(LinkedAssetId)
            ? "Info"
            : "Good";

    [System.Text.Json.Serialization.JsonIgnore]
    public string UploaderDisplay => string.IsNullOrWhiteSpace(UploaderName)
        ? "Uploader not provided"
        : $"Uploaded by {UploaderName}";

    [System.Text.Json.Serialization.JsonIgnore]
    public string UploadedAtDisplay => UploadedAt is null
        ? "Upload time unavailable"
        : UploadedAt.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
}
