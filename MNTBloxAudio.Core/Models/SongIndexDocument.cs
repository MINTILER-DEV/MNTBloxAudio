using System.Text.Json.Serialization;

namespace MNTBloxAudio.Core.Models;

public sealed class SongIndexDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("songs")]
    public List<SongIndexEntry> Songs { get; init; } = [];
}
