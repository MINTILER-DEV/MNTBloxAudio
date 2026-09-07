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

    public SettingsStore()
    {
        settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MNTBloxAudio",
            "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions).ConfigureAwait(false);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        // Snapshot before awaiting so rapid toggles cannot mutate an in-flight serialization.
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        await saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var temporary = settingsPath + ".tmp";
            await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
            File.Move(temporary, settingsPath, true);
        }
        finally { saveLock.Release(); }
    }
}
