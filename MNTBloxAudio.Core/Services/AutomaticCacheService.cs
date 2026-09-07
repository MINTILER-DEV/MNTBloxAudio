using System.Security.Cryptography;
using System.Text.Json;

namespace MNTBloxAudio.Core.Services;

public sealed record PreparedCacheRule(string AssetId, string OriginalHash, string ReplacementHash, string LocalPath);
public sealed record CacheSyncResult(HashSet<string> AppliedAssets, HashSet<string> PendingAssets, Dictionary<string, string> Errors);

/// <summary>Serial, durable cache reconciliation. Never infer asset identity from request timing.</summary>
public sealed class AutomaticCacheService
{
    public sealed record Replacement(string AssetId, string OriginalHash, string ReplacementHash);
    private readonly string cacheDirectory;
    private readonly string backupDirectory;
    private readonly string manifestPath;
    private readonly Dictionary<string, Replacement> replacements;
    private readonly bool recoveryBlocked;
    public string? RecoveryNotice { get; }

    public AutomaticCacheService(string? cacheDirectory = null, string? stateDirectory = null)
    {
        this.cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "Roblox", "sounds");
        var state = stateDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MNTBloxAudio");
        backupDirectory = Path.Combine(state, "sound-cache-backups");
        manifestPath = Path.Combine(backupDirectory, "replacements.json");
        Directory.CreateDirectory(backupDirectory);
        var result = RecoverableJsonFile.Load(manifestPath,
            () => new Dictionary<string, Replacement>(StringComparer.OrdinalIgnoreCase),
            validate: records => records.All(entry => entry.Key.StartsWith("RBX", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(entry.Key) == entry.Key && entry.Value is not null
                && !string.IsNullOrWhiteSpace(entry.Value.AssetId)
                && IsHash(entry.Value.OriginalHash) && IsHash(entry.Value.ReplacementHash)));
        replacements = new(result.Value, StringComparer.OrdinalIgnoreCase);
        recoveryBlocked = result.ResetDamagedFile && Directory.EnumerateFiles(backupDirectory)
            .Any(path => path.EndsWith(".original", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));
        RecoveryNotice = recoveryBlocked
            ? "Cache recovery records are damaged. Automatic cache changes are paused; your original audio backups are untouched."
            : result.Notice;
        if (result.Notice is not null && !recoveryBlocked) SaveManifest();
    }

    private static bool IsHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

    public CacheSyncResult Synchronize(IReadOnlyList<PreparedCacheRule> desired)
    {
        var applied = new HashSet<string>();
        var pending = new HashSet<string>();
        var errors = new Dictionary<string, string>();
        if (recoveryBlocked)
        {
            errors["*"] = RecoveryNotice!;
            return new(applied, pending, errors);
        }
        if (!Directory.Exists(cacheDirectory)) return new(applied, pending, errors);

        foreach (var path in Directory.EnumerateFiles(cacheDirectory, "RBX*"))
        {
            var name = Path.GetFileName(path);
            replacements.TryGetValue(name, out var previous);
            var operationAsset = previous?.AssetId;
            var cacheOpened = false;
            try
            {
                // FileShare.None prevents writing while Roblox holds a handle, even a shared read handle.
                using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                cacheOpened = true;
                var hash = Hash(file);
                if (previous is not null)
                {
                    var keep = desired.Any(rule => rule.AssetId == previous.AssetId && rule.ReplacementHash == previous.ReplacementHash);
                    if (hash == previous.ReplacementHash && keep)
                    {
                        applied.Add(previous.AssetId);
                        continue;
                    }
                    if (hash == previous.ReplacementHash)
                    {
                        // Exclusive access is the file-specific gate. Unrelated Roblox audio must
                        // not prevent restoration; audio already decoded in memory is unaffected.
                        var backup = Path.Combine(backupDirectory, previous.OriginalHash + ".original");
                        if (!File.Exists(backup)) throw new IOException("Original backup is missing. Restore it to the sound-cache-backups folder to retry.");
                        WriteVerified(file, backup, previous.OriginalHash);
                        hash = previous.OriginalHash;
                    }
                    // Roblox may have evicted/reused this filename. Never restore over unrelated bytes.
                    replacements.Remove(name);
                    SaveManifest();
                }

                var match = desired.FirstOrDefault(rule => rule.OriginalHash == hash && rule.ReplacementHash != hash);
                if (match is null || !File.Exists(match.LocalPath)) continue;
                operationAsset = match.AssetId;
                var backupPath = Path.Combine(backupDirectory, hash + ".original");
                if (!File.Exists(backupPath))
                {
                    file.Position = 0;
                    using var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    file.CopyTo(backup);
                    backup.Flush(true);
                }
                // Persist recovery ownership before modifying audio, including when a rule is later removed.
                replacements[name] = new(match.AssetId, hash, match.ReplacementHash);
                SaveManifest();
                WriteVerified(file, match.LocalPath, match.ReplacementHash);
                applied.Add(match.AssetId);
            }
            catch (IOException exception) when (!cacheOpened && (exception.HResult & 0xFFFF) is 32 or 33)
            {
                if (operationAsset is not null) pending.Add(operationAsset);
            }
            catch (IOException exception)
            {
                if (operationAsset is not null) errors[operationAsset] = exception.Message;
            }
            catch (UnauthorizedAccessException)
            {
                if (operationAsset is not null) errors[operationAsset] = "Access denied to the cache file or original backup. Check file permissions.";
            }
        }
        return new(applied, pending, errors);
    }

    public void ImportLegacyBackups(IReadOnlyList<PreparedCacheRule> knownRules)
    {
        if (recoveryBlocked) return;
        if (!Directory.Exists(cacheDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(cacheDirectory, "RBX*"))
        {
            var name = Path.GetFileName(path);
            var legacy = Path.Combine(backupDirectory, name + ".bak");
            if (replacements.ContainsKey(name) || !File.Exists(legacy)) continue;
            try
            {
                using var file = File.OpenRead(path);
                using var backup = File.OpenRead(legacy);
                var currentHash = Hash(file);
                var originalHash = Hash(backup);
                var match = knownRules.FirstOrDefault(rule => rule.OriginalHash == originalHash && rule.ReplacementHash == currentHash);
                if (match is null) continue;
                File.Copy(legacy, Path.Combine(backupDirectory, originalHash + ".original"), true);
                replacements[name] = new(match.AssetId, originalHash, currentHash);
                SaveManifest();
            }
            catch (IOException) { /* Retry on the next pass when the cache file is released. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void SaveManifest()
    {
        RecoverableJsonFile.Save(manifestPath, JsonSerializer.Serialize(replacements));
    }

    private static string Hash(Stream stream)
    {
        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteVerified(FileStream destination, string sourcePath, string expectedHash)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Hash(source) != expectedHash) throw new IOException("Audio integrity check failed: the source or original backup has changed. No audio was overwritten.");
        // Keep the current bytes for rollback if a write fails midway.
        using var rollback = new MemoryStream();
        destination.Position = 0;
        destination.CopyTo(rollback);
        try
        {
            source.Position = 0;
            destination.Position = 0;
            source.CopyTo(destination);
            destination.SetLength(source.Length);
            destination.Flush(true);
        }
        catch
        {
            rollback.Position = 0;
            destination.Position = 0;
            rollback.CopyTo(destination);
            destination.SetLength(rollback.Length);
            destination.Flush(true);
            throw;
        }
    }
}
