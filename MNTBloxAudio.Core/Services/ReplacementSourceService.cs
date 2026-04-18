using System.Net.Http;
using System.Security.Cryptography;
using MNTBloxAudio.Core.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class ReplacementSourceService
{
    private static readonly HttpClient HttpClient = new();
    private readonly string cacheDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MNTBloxAudio",
        "replacement-source-cache");

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
            return await ResolveRemoteAsync(uri, forceRefresh, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(source))
        {
            return null;
        }

        var fileInfo = new FileInfo(source);
        return new ResolvedReplacementSource
        {
            Source = source,
            LocalPath = fileInfo.FullName,
            DisplayName = fileInfo.Name,
            Length = fileInfo.Length,
            IsRemote = false,
        };
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
}
