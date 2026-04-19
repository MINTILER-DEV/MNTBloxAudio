using System.Net.Http;
using System.Diagnostics;
using System.Security.Cryptography;
using System.IO.Compression;
using NAudio.MediaFoundation;
using NAudio.Wave;
using MNTBloxAudio.Core.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class ReplacementSourceService
{
    private static readonly HttpClient HttpClient = new();
    private const string FfmpegDownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string FfmpegSha256Url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256";
    private readonly string cacheDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MNTBloxAudio",
        "replacement-source-cache");
    private readonly string convertedCacheDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MNTBloxAudio",
        "replacement-source-cache",
        "converted");
    private readonly string toolsDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MNTBloxAudio",
        "tools");

    private static readonly HashSet<string> PassthroughExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".wav",
        ".ogg",
    };

    public bool IsRemoteSource(string? source)
    {
        return TryGetRemoteUri(source, out _);
    }

    public async Task<ResolvedReplacementSource?> ResolveAsync(
        string? source,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (TryGetRemoteUri(source, out var uri))
        {
            var remoteSource = await ResolveRemoteAsync(uri, forceRefresh, cancellationToken).ConfigureAwait(false);
            return await NormalizeResolvedSourceAsync(remoteSource, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(source))
        {
            return null;
        }

        var fileInfo = new FileInfo(source);
        var localSource = new ResolvedReplacementSource
        {
            Source = source,
            LocalPath = fileInfo.FullName,
            DisplayName = fileInfo.Name,
            Length = fileInfo.Length,
            IsRemote = false,
        };

        return await NormalizeResolvedSourceAsync(localSource, cancellationToken).ConfigureAwait(false);
    }

    public string GetSourceDisplayName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "No source";
        }

        if (TryGetRemoteUri(source, out var uri))
        {
            if (!string.IsNullOrWhiteSpace(Path.GetFileName(uri.AbsolutePath)))
            {
                return Path.GetFileName(uri.AbsolutePath);
            }

            return uri.Host;
        }

        return Path.GetFileName(source);
    }

    private async Task<ResolvedReplacementSource> ResolveRemoteAsync(
        Uri uri,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectoryPath);

        var cachePath = Path.Combine(cacheDirectoryPath, BuildCacheFileName(uri));
        if (!forceRefresh && File.Exists(cachePath))
        {
            var cachedInfo = new FileInfo(cachePath);
            return new ResolvedReplacementSource
            {
                Source = uri.AbsoluteUri,
                LocalPath = cachedInfo.FullName,
                DisplayName = GetSourceDisplayName(uri.AbsoluteUri),
                Length = cachedInfo.Length,
                IsRemote = true,
            };
        }

        var tempPath = cachePath + ".tmp";

        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, cachePath, overwrite: true);

        var fileInfo = new FileInfo(cachePath);
        return new ResolvedReplacementSource
        {
            Source = uri.AbsoluteUri,
            LocalPath = fileInfo.FullName,
            DisplayName = GetSourceDisplayName(uri.AbsoluteUri),
            Length = fileInfo.Length,
            IsRemote = true,
        };
    }

    private async Task<ResolvedReplacementSource> NormalizeResolvedSourceAsync(
        ResolvedReplacementSource source,
        CancellationToken cancellationToken)
    {
        if (ShouldKeepOriginalFile(source.LocalPath))
        {
            return source;
        }

        return await ConvertToMp3Async(source, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResolvedReplacementSource> ConvertToMp3Async(
        ResolvedReplacementSource source,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(convertedCacheDirectoryPath);

        var sourceInfo = new FileInfo(source.LocalPath);
        var convertedPath = Path.Combine(convertedCacheDirectoryPath, BuildConvertedFileName(sourceInfo));

        if (!File.Exists(convertedPath))
        {
            var tempPath = BuildTemporaryOutputPath(convertedPath);
            Exception? builtInFailure = null;
            try
            {
                await TryConvertWithBuiltInPipelineAsync(source.LocalPath, tempPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                builtInFailure = exception;

                try
                {
                    await TryConvertWithFfmpegAsync(source.LocalPath, tempPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ffmpegFailure)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup failures.
                    }

                    throw new InvalidOperationException(
                        $"The replacement source '{source.DisplayName}' could not be converted to MP3 automatically. Built-in decoder: {SummarizeExceptionMessage(builtInFailure)}. ffmpeg: {SummarizeExceptionMessage(ffmpegFailure)}");
                }
            }

            File.Move(tempPath, convertedPath, overwrite: true);
        }

        var convertedInfo = new FileInfo(convertedPath);
        return new ResolvedReplacementSource
        {
            Source = source.Source,
            LocalPath = convertedInfo.FullName,
            DisplayName = Path.GetFileNameWithoutExtension(source.DisplayName) + ".mp3",
            Length = convertedInfo.Length,
            IsRemote = source.IsRemote,
            IsConverted = true,
        };
    }

    private static bool TryGetRemoteUri(string? source, out Uri uri)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out uri!))
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        uri = null!;
        return false;
    }

    private static string BuildCacheFileName(Uri uri)
    {
        var uriBytes = System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri);
        var hash = Convert.ToHexString(SHA256.HashData(uriBytes));
        var extension = Path.GetExtension(uri.AbsolutePath);

        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = ".bin";
        }

        return $"{hash}{extension}";
    }

    private static bool ShouldKeepOriginalFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return PassthroughExtensions.Contains(extension);
    }

    private static string BuildConvertedFileName(FileInfo fileInfo)
    {
        var cacheKey = string.Join(
            "|",
            fileInfo.FullName,
            fileInfo.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fileInfo.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(cacheKey);
        var hash = Convert.ToHexString(SHA256.HashData(keyBytes));
        return $"{hash}.mp3";
    }

    private static async Task TryConvertWithBuiltInPipelineAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new AudioFileReader(sourcePath);
            MediaFoundationEncoder.EncodeToMp3(reader, outputPath);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryConvertWithFfmpegAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = await EnsureFfmpegPathAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new InvalidOperationException("ffmpeg was not found.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-nostdin -v error -y -i \"{sourcePath}\" -vn -c:a libmp3lame -b:a 192k \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var ffmpegError = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(ffmpegError)
                ? "ffmpeg conversion failed."
                : ffmpegError.Trim());
        }
    }

    private async Task<string?> EnsureFfmpegPathAsync(CancellationToken cancellationToken)
    {
        var existingPath = ResolveExistingFfmpegPath();
        if (!string.IsNullOrWhiteSpace(existingPath)
            && await IsFfmpegUsableAsync(existingPath, cancellationToken).ConfigureAwait(false))
        {
            return existingPath;
        }

        var managedRootPath = Path.Combine(toolsDirectoryPath, "ffmpeg");
        if (Directory.Exists(managedRootPath))
        {
            try
            {
                Directory.Delete(managedRootPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures and try a fresh download anyway.
            }
        }

        return await DownloadManagedFfmpegAsync(cancellationToken).ConfigureAwait(false);
    }

    private string? ResolveExistingFfmpegPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var candidate = Path.Combine(directory, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Ignore bad PATH entries.
                }
            }
        }

        var managedCandidate = Path.Combine(toolsDirectoryPath, "ffmpeg", "ffmpeg.exe");
        if (File.Exists(managedCandidate))
        {
            return managedCandidate;
        }

        var commonCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "global", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
        };

        return commonCandidates.FirstOrDefault(File.Exists);
    }

    private async Task<string> DownloadManagedFfmpegAsync(CancellationToken cancellationToken)
    {
        var ffmpegRootDirectory = Path.Combine(toolsDirectoryPath, "ffmpeg");
        var downloadDirectory = Path.Combine(toolsDirectoryPath, "downloads");
        Directory.CreateDirectory(ffmpegRootDirectory);
        Directory.CreateDirectory(downloadDirectory);

        var archivePath = Path.Combine(downloadDirectory, "ffmpeg-release-essentials.zip");
        var tempArchivePath = archivePath + ".tmp";
        var extractRootPath = Path.Combine(ffmpegRootDirectory, "extract");

        using (var response = await HttpClient.GetAsync(FfmpegDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                   .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = File.Create(tempArchivePath);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempArchivePath, archivePath, overwrite: true);
        await VerifyDownloadedArchiveAsync(archivePath, cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(extractRootPath))
        {
            Directory.Delete(extractRootPath, recursive: true);
        }

        ZipFile.ExtractToDirectory(archivePath, extractRootPath, overwriteFiles: true);

        var discoveredPath = Directory
            .EnumerateFiles(extractRootPath, "ffmpeg.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(discoveredPath))
        {
            throw new InvalidOperationException("Downloaded ffmpeg package did not contain ffmpeg.exe.");
        }

        var discoveredBinDirectory = Path.GetDirectoryName(discoveredPath)!;
        foreach (var filePath in Directory.EnumerateFiles(discoveredBinDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var copiedFilePath = Path.Combine(ffmpegRootDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, copiedFilePath, overwrite: true);
        }

        var destinationPath = Path.Combine(ffmpegRootDirectory, "ffmpeg.exe");
        if (!await IsFfmpegUsableAsync(destinationPath, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Downloaded ffmpeg is not usable after extraction.");
        }

        return destinationPath;
    }

    private static async Task VerifyDownloadedArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        var expectedHash = (await HttpClient.GetStringAsync(FfmpegSha256Url, cancellationToken).ConfigureAwait(false)).Trim();
        await using var stream = File.OpenRead(archivePath);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded ffmpeg archive failed SHA-256 verification.");
        }
    }

    private static async Task<bool> IsFfmpegUsableAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = "-version",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return process.ExitCode == 0
                && (!string.IsNullOrWhiteSpace(stdout) || string.IsNullOrWhiteSpace(stderr));
        }
        catch
        {
            return false;
        }
    }

    private static string SummarizeExceptionMessage(Exception? exception)
    {
        if (exception is null)
        {
            return "unknown failure";
        }

        var message = exception.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return exception.GetType().Name;
        }

        message = message.Replace(Environment.NewLine, " ").Replace('\r', ' ').Replace('\n', ' ');
        return message.Length <= 260 ? message : message[..260] + "...";
    }

    private static string BuildTemporaryOutputPath(string finalPath)
    {
        var directory = Path.GetDirectoryName(finalPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(finalPath);
        var extension = Path.GetExtension(finalPath);
        return Path.Combine(directory, $"{fileNameWithoutExtension}.tmp{extension}");
    }
}
