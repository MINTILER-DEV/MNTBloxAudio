using System.Security.Cryptography;
using MNTBloxAudio.Core.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class RobloxSoundCacheService
{
    private readonly string soundCacheDirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "Roblox",
        "sounds");
    private readonly string backupDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MNTBloxAudio",
        "sound-cache-backups");

    private readonly Dictionary<string, (DateTimeOffset LastWriteTime, long Length)> knownFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset LastWriteTime, long Length, string Hash)> hashCache = new(StringComparer.OrdinalIgnoreCase);
    private bool initialized;

    public IReadOnlyList<RobloxSoundCacheEntry> PollCacheChanges()
    {
        if (!Directory.Exists(soundCacheDirectoryPath))
        {
            return [];
        }

        var files = Directory
            .EnumerateFiles(soundCacheDirectoryPath, "RBX*", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new RobloxSoundCacheEntry
                {
                    FileName = info.Name,
                    FullPath = info.FullName,
                    Length = info.Length,
                    LastWriteTime = info.LastWriteTime,
                };
            })
            .OrderBy(entry => entry.LastWriteTime)
            .ToList();

        if (!initialized)
        {
            foreach (var file in files)
            {
                knownFiles[file.FullPath] = (file.LastWriteTime, file.Length);
            }

            initialized = true;
            return [];
        }

        var changes = new List<RobloxSoundCacheEntry>();
        foreach (var file in files)
        {
            var isNewFile = !knownFiles.TryGetValue(file.FullPath, out var existing);
            if (isNewFile
                || existing.LastWriteTime != file.LastWriteTime
                || existing.Length != file.Length)
            {
                knownFiles[file.FullPath] = (file.LastWriteTime, file.Length);
                changes.Add(new RobloxSoundCacheEntry
                {
                    FileName = file.FileName,
                    FullPath = file.FullPath,
                    IsNewlyDetected = isNewFile,
                    Length = file.Length,
                    LastWriteTime = file.LastWriteTime,
                    Sha256 = ComputeFileHash(file.FullPath),
                });
            }
        }

        return changes;
    }

    public IReadOnlyList<RobloxSoundCacheEntry> GetSoundCacheSnapshot()
    {
        if (!Directory.Exists(soundCacheDirectoryPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(soundCacheDirectoryPath, "RBX*", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var info = new FileInfo(path);
                knownFiles[info.FullName] = (info.LastWriteTime, info.Length);
                return new RobloxSoundCacheEntry
                {
                    FileName = info.Name,
                    FullPath = info.FullName,
                    IsNewlyDetected = false,
                    Length = info.Length,
                    LastWriteTime = info.LastWriteTime,
                    Sha256 = ComputeFileHash(info.FullName),
                };
            })
            .OrderByDescending(entry => entry.LastWriteTime)
            .ToList();
    }

    public string ComputeFileHash(string filePath)
    {
        var info = new FileInfo(filePath);
        if (hashCache.TryGetValue(filePath, out var existing)
            && existing.LastWriteTime == info.LastWriteTime
            && existing.Length == info.Length)
        {
            return existing.Hash;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = SHA256.Create();
        var hash = Convert.ToHexString(sha256.ComputeHash(stream));

        hashCache[filePath] = (info.LastWriteTime, info.Length, hash);
        return hash;
    }

    public bool ReplaceSoundFile(string cacheFilePath, string replacementFilePath)
    {
        try
        {
            Directory.CreateDirectory(backupDirectoryPath);

            var backupPath = Path.Combine(backupDirectoryPath, Path.GetFileName(cacheFilePath) + ".bak");
            if (!File.Exists(backupPath))
            {
                File.Copy(cacheFilePath, backupPath, overwrite: false);
            }

            OverwriteFileContents(replacementFilePath, cacheFilePath);

            var updatedInfo = new FileInfo(cacheFilePath);
            hashCache.Remove(cacheFilePath);
            knownFiles[cacheFilePath] = (updatedInfo.LastWriteTime, updatedInfo.Length);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool RestoreSoundFile(string cacheFilePath)
    {
        var backupPath = Path.Combine(backupDirectoryPath, Path.GetFileName(cacheFilePath) + ".bak");
        if (!File.Exists(backupPath))
        {
            return false;
        }

        try
        {
            OverwriteFileContents(backupPath, cacheFilePath);

            var updatedInfo = new FileInfo(cacheFilePath);
            hashCache.Remove(cacheFilePath);
            knownFiles[cacheFilePath] = (updatedInfo.LastWriteTime, updatedInfo.Length);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void OverwriteFileContents(string sourceFilePath, string destinationFilePath)
    {
        using var sourceStream = new FileStream(
            sourceFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var destinationStream = new FileStream(
            destinationFilePath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);

        destinationStream.SetLength(0);
        sourceStream.CopyTo(destinationStream);
        destinationStream.Flush(flushToDisk: true);
    }
}
