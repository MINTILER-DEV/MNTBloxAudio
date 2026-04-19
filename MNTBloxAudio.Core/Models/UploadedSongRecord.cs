using System.Text.Json.Serialization;

namespace MNTBloxAudio.Core.Models;

public sealed class UploadedSongRecord
{
    public string Code { get; set; } = string.Empty;

    public string SongName { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string UploaderName { get; set; } = string.Empty;

    public string UploadedByDeviceId { get; set; } = string.Empty;

    public string AudioUrl { get; set; } = string.Empty;

    public DateTimeOffset? UploadedAt { get; set; }

    [JsonIgnore]
    public string SummaryDisplay => string.IsNullOrWhiteSpace(Artist)
        ? SongName
        : $"{SongName} - {Artist}";
}
