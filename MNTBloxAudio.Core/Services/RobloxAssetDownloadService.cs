using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using MNTBloxAudio.Core.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class RobloxAssetDownloadService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
    });

    public async Task<DownloadedAssetInfo> DownloadAssetInfoAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var curlAsset = await TryDownloadWithCurlAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (curlAsset is not null)
        {
            return curlAsset;
        }

        using var response = await HttpClient.GetAsync(
            $"https://assetdelivery.roblox.com/v1/asset?id={assetId}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var sha256 = SHA256.Create();

        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalBytes += bytesRead;
        }

        sha256.TransformFinalBlock([], 0, 0);

        return new DownloadedAssetInfo
        {
            AssetId = assetId,
            Length = totalBytes,
            Sha256 = Convert.ToHexString(sha256.Hash ?? []),
        };
    }

    private static async Task<DownloadedAssetInfo?> TryDownloadWithCurlAsync(string assetId, CancellationToken cancellationToken)
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"mntbloxaudio-asset-{assetId}-{Guid.NewGuid():N}.bin");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = $"--compressed -L --fail --silent --show-error -o \"{tempFilePath}\" \"https://assetdelivery.roblox.com/v1/asset?id={assetId}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardError = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError)
                    ? $"curl.exe exited with code {process.ExitCode}."
                    : standardError.Trim());
            }

            if (!File.Exists(tempFilePath))
            {
                throw new InvalidOperationException("curl.exe finished without producing a download file.");
            }

            var fileInfo = new FileInfo(tempFilePath);
            using var stream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha256 = SHA256.Create();

            return new DownloadedAssetInfo
            {
                AssetId = assetId,
                Length = fileInfo.Length,
                Sha256 = Convert.ToHexString(await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false)),
            };
        }
        catch (Win32Exception)
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Ignore temp cleanup failures.
            }
        }
    }
}
