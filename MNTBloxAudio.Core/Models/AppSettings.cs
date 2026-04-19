using System.Text.Json.Serialization;

namespace MNTBloxAudio.Core.Models;

public sealed class AppSettings
{
    public string? PreferredOutputDeviceId { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string SongIndexBaseUrl { get; set; } = string.Empty;

    public bool AutoReplaceOnRobloxAudioActivity { get; set; }

    public bool AutoReplaceOnDetection { get; set; } = true;

    public bool AutoApplyCacheReplacements { get; set; }

    public bool AutoMuteRobloxDuringPlayback { get; set; } = true;

    public bool AutoRestoreRobloxAfterPlayback { get; set; } = true;

    public int OutputVolumePercent { get; set; } = 100;

    public bool EnableProxyFallback { get; set; }

    public bool EnableExperimentalProxyReplacement { get; set; }

    public int ProxyPort { get; set; } = 8877;

    [JsonInclude]
    public List<ReplacementRule> Rules { get; set; } = [];

    [JsonInclude]
    public List<UploadedSongRecord> UploadedSongs { get; set; } = [];
}
