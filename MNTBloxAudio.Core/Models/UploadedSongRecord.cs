using System.Text.Json.Serialization;

namespace MNTBloxAudio.Core.Models;

public sealed class UploadedSongRecord
{
    public string Code { get; set; } = string.Empty;

    public string LinkedAssetId { get; set; } = string.Empty;

    public string SongName { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string UploaderName { get; set; } = string.Empty;

    public string UploadedByDeviceId { get; set; } = string.Empty;

    public string AudioUrl { get; set; } = string.Empty;

    public DateTimeOffset? UploadedAt { get; set; }

    [JsonIgnore]
    public string LinkedAssetIdDisplay => string.IsNullOrWhiteSpace(LinkedAssetId)
        ? "No linked Roblox ID"
        : $"Linked Roblox ID {LinkedAssetId}";

    [JsonIgnore]
    public string SummaryDisplay => string.IsNullOrWhiteSpace(Artist)
        ? SongName
        : $"{SongName} - {Artist}";

    [JsonIgnore]
    public bool HasPlayableAudio => Uri.TryCreate(AudioUrl, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public string StatusDisplay => !HasPlayableAudio
        ? "Broken Audio"
        : string.IsNullOrWhiteSpace(LinkedAssetId)
            ? "Direct Only"
            : "Linked";

    [JsonIgnore]
    public string StatusTone => !HasPlayableAudio
        ? "Warn"
        : string.IsNullOrWhiteSpace(LinkedAssetId)
            ? "Info"
            : "Good";

    [JsonIgnore]
    public string UploaderDisplay => string.IsNullOrWhiteSpace(UploaderName)
        ? "Uploader not provided"
        : $"Uploaded by {UploaderName}";

    [JsonIgnore]
    public string UploadedAtDisplay => UploadedAt is null
        ? "Saved locally"
        : UploadedAt.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
}
