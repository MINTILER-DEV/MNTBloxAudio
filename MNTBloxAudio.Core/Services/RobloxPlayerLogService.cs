using System.Text.RegularExpressions;
using MNTBloxAudio.Core.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class RobloxPlayerLogService
{
    private static readonly Regex AssetResolutionRegex = new(
        @"AssetResolutionWorkflow to get assetid:\s+(https://assetdelivery\.roblox\.com/v1/asset\?id=([0-9]+)[^\s]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FailedSoundRegex = new(
        @"Failed to load sound rbxassetid://([0-9]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string logDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox",
        "logs");

    private string? currentLogPath;
    private long currentOffset;
    private bool hasAttachedToLog;

    public IReadOnlyList<RobloxAssetResolutionInfo> PollResolvedAssets()
    {
        var latestLogPath = GetLatestPlayerLogPath();
        if (string.IsNullOrWhiteSpace(latestLogPath) || !File.Exists(latestLogPath))
        {
            return [];
        }

        var fileInfo = new FileInfo(latestLogPath);
        if (!string.Equals(currentLogPath, latestLogPath, StringComparison.OrdinalIgnoreCase))
        {
            currentLogPath = latestLogPath;
            currentOffset = hasAttachedToLog ? 0 : fileInfo.Length;
            hasAttachedToLog = true;
        }
        else if (fileInfo.Length < currentOffset)
        {
            currentOffset = 0;
        }

        using var stream = new FileStream(
            latestLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        stream.Seek(currentOffset, SeekOrigin.Begin);
        var newContent = reader.ReadToEnd();
        currentOffset = stream.Position;

        if (string.IsNullOrWhiteSpace(newContent))
        {
            return [];
        }

        var events = new List<RobloxAssetResolutionInfo>();
        foreach (var line in newContent.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var assetMatch = AssetResolutionRegex.Match(line);
            if (assetMatch.Success)
            {
                events.Add(new RobloxAssetResolutionInfo
                {
                    AssetId = assetMatch.Groups[2].Value,
                    Url = assetMatch.Groups[1].Value,
                    Timestamp = DateTimeOffset.Now,
                });

                continue;
            }

            var failedSoundMatch = FailedSoundRegex.Match(line);
            if (failedSoundMatch.Success)
            {
                var assetId = failedSoundMatch.Groups[1].Value;
                events.Add(new RobloxAssetResolutionInfo
                {
                    AssetId = assetId,
                    Url = $"rbxassetid://{assetId}",
                    Timestamp = DateTimeOffset.Now,
                });
            }
        }

        return events;
    }

    private string? GetLatestPlayerLogPath()
    {
        if (!Directory.Exists(logDirectoryPath))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(logDirectoryPath, "*_Player_*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
