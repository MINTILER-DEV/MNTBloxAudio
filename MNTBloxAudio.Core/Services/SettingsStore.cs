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
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions).ConfigureAwait(false);
    }
}
