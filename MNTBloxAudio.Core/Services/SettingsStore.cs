using System.Text.Json;
using MNTBloxAudio.Core.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string settingsPath;
    private readonly SemaphoreSlim saveLock = new(1, 1);

    public string? RecoveryNotice { get; private set; }

    public SettingsStore(string? stateDirectory = null)
    {
        settingsPath = Path.Combine(stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MNTBloxAudio"),
            "settings.json");
    }

    public Task<AppSettings> LoadAsync()
    {
        var result = RecoverableJsonFile.Load(settingsPath, () => new AppSettings(), SerializerOptions);
        RecoveryNotice = result.Notice;
        var settings = result.Value;
        settings.Rules = (settings.Rules ?? []).Where(rule => rule is not null).ToList();
        settings.UploadedSongs = (settings.UploadedSongs ?? []).Where(song => song is not null).ToList();
        foreach (var rule in settings.Rules)
        {
            rule.Name ??= "My audio";
            rule.AssetIdPattern ??= string.Empty;
            rule.FilePath ??= string.Empty;
        }
        return Task.FromResult(settings);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        // Snapshot before awaiting so rapid toggles cannot mutate an in-flight serialization.
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        await saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => RecoverableJsonFile.Save(settingsPath, json)).ConfigureAwait(false);
        }
        finally { saveLock.Release(); }
    }
}
