using System.Text;
using System.Text.Json;

namespace MNTBloxAudio.Core.Services;

internal sealed record JsonRecoveryResult<T>(T Value, bool ResetDamagedFile, string? Notice);

internal static class RecoverableJsonFile
{
    public static JsonRecoveryResult<T> Load<T>(string path, Func<T> createDefault, JsonSerializerOptions? options = null, Func<T, bool>? validate = null)
        where T : class
    {
        var damaged = false;
        foreach (var candidate in new[] { path, path + ".backup" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var value = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), options);
                if (value is null || validate?.Invoke(value) == false) throw new JsonException("Invalid saved document.");
                if (candidate != path)
                    return new(value, false, $"Recovered {Path.GetFileName(path)} from its saved backup.");
                return new(value, false, null);
            }
            catch (JsonException)
            {
                // Preserve the exact damaged bytes before a later save replaces the document.
                File.Copy(candidate, candidate + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
                damaged = true;
            }
        }
        return new(createDefault(), damaged, damaged ? $"Could not recover {Path.GetFileName(path)}. The damaged file was preserved." : null);
    }

    public static void Save(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Both copies contain the newest committed state before any cache audio is changed.
        WriteAtomic(path, json);
        WriteAtomic(path + ".backup", json);
    }

    private static void WriteAtomic(string path, string json)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(json));
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }
}
