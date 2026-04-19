using System.Net;
using System.Net.Http.Json;
using MNTBloxAudio.Core.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class SongIndexService
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
    });

    private const string DefaultSiteBaseUrl = "https://mntbloxindex.vercel.app/";

    public string GetDefaultSiteBaseUrl() => DefaultSiteBaseUrl;

    public string GetSiteBaseUrl(string? configuredBaseUrl) => BuildSiteBaseUri(configuredBaseUrl).AbsoluteUri;

    public string GetIndexApiUrl(string? configuredBaseUrl) => new Uri(BuildSiteBaseUri(configuredBaseUrl), "api/index").AbsoluteUri;

    public static bool LooksLikeSongCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 6 && trimmed.All(character => char.IsLetter(character));
    }

    public async Task<SongIndexEntry?> ResolveSongCodeAsync(
        string code,
        string? configuredBaseUrl = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeSongCode(code);
        if (!LooksLikeSongCode(normalizedCode))
        {
            return null;
        }

        var document = await LoadIndexAsync(configuredBaseUrl, cancellationToken).ConfigureAwait(false);
        var match = document.Songs.FirstOrDefault(entry =>
            string.Equals(entry.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : NormalizeUrls(match, configuredBaseUrl);
    }

    public async Task<IReadOnlyList<SongIndexEntry>> SearchSongsAsync(
        string? query,
        string? configuredBaseUrl = null,
        CancellationToken cancellationToken = default)
    {
        var document = await LoadIndexAsync(configuredBaseUrl, cancellationToken).ConfigureAwait(false);
        var normalizedQuery = (query ?? string.Empty).Trim();

        var normalizedSongs = document.Songs
            .Select(entry => NormalizeUrls(entry, configuredBaseUrl))
            .ToList();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return normalizedSongs
                .OrderByDescending(entry => entry.UploadedAt ?? DateTimeOffset.MinValue)
                .ThenBy(entry => entry.SongName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return normalizedSongs
            .Where(entry => MatchesSearch(entry, normalizedQuery))
            .OrderByDescending(entry => entry.UploadedAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.SongName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<UploadedSongRecord> SubmitSongLinkAsync(
        string audioUrl,
        string songName,
        string artist,
        string uploaderName,
        string deviceId,
        string? configuredBaseUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Audio URL must be an absolute http/https link.");
        }

        using var response = await HttpClient.PostAsJsonAsync(new Uri(BuildSiteBaseUri(configuredBaseUrl), "api/upload"), new
        {
            audioUrl = parsedUri.AbsoluteUri,
            songName = songName.Trim(),
            artist = artist.Trim(),
            uploaderName = (uploaderName ?? string.Empty).Trim(),
            deviceId = deviceId.Trim(),
        }, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(body) ?? "Upload request failed.");
        }

        var uploadedEntry = System.Text.Json.JsonSerializer.Deserialize<SongIndexEntry>(body, JsonSerializerOptions);
        if (uploadedEntry is null)
        {
            throw new InvalidOperationException("Upload succeeded but the response body was empty.");
        }

        var normalized = NormalizeUrls(uploadedEntry, configuredBaseUrl);
        return new UploadedSongRecord
        {
            Code = normalized.Code,
            SongName = normalized.SongName,
            Artist = normalized.Artist,
            UploaderName = normalized.UploaderName,
            UploadedByDeviceId = normalized.UploadedByDeviceId,
            AudioUrl = normalized.AudioUrl,
            UploadedAt = normalized.UploadedAt,
        };
    }

    public async Task DeleteSongAsync(
        string code,
        string deviceId,
        string? configuredBaseUrl = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeSongCode(code);
        if (!LooksLikeSongCode(normalizedCode))
        {
            throw new InvalidOperationException("Song code must be exactly six letters.");
        }

        var requestUri = new Uri(BuildSiteBaseUri(configuredBaseUrl), $"api/songs/{normalizedCode}?deviceId={Uri.EscapeDataString(deviceId)}");
        using var response = await HttpClient.DeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(ExtractErrorMessage(body) ?? "Delete request failed.");
    }

    private static async Task<SongIndexDocument> LoadIndexAsync(string? configuredBaseUrl, CancellationToken cancellationToken)
    {
        var indexUri = new Uri(BuildSiteBaseUri(configuredBaseUrl), "api/index");
        var document = await HttpClient.GetFromJsonAsync<SongIndexDocument>(indexUri, cancellationToken).ConfigureAwait(false);
        return document ?? new SongIndexDocument();
    }

    private static SongIndexEntry NormalizeUrls(SongIndexEntry entry, string? configuredBaseUrl)
    {
        var siteBaseUri = BuildSiteBaseUri(configuredBaseUrl);
        var resolvedAudioUri = Uri.TryCreate(entry.AudioUrl, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(siteBaseUri, entry.AudioUrl);

        return new SongIndexEntry
        {
            Code = NormalizeSongCode(entry.Code),
            SongName = entry.SongName,
            Artist = entry.Artist,
            UploaderName = entry.UploaderName,
            UploadedByDeviceId = entry.UploadedByDeviceId,
            AudioUrl = resolvedAudioUri.AbsoluteUri,
            UploadedAt = entry.UploadedAt,
        };
    }

    private static Uri BuildSiteBaseUri(string? configuredBaseUrl)
    {
        if (Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredUri)
            && (configuredUri.Scheme == Uri.UriSchemeHttp || configuredUri.Scheme == Uri.UriSchemeHttps))
        {
            var normalizedBaseUrl = configuredUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? configuredUri.AbsoluteUri
                : configuredUri.AbsoluteUri + "/";

            return new Uri(normalizedBaseUrl);
        }

        return new Uri(DefaultSiteBaseUrl);
    }

    private static string NormalizeSongCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool MatchesSearch(SongIndexEntry entry, string query)
    {
        return Contains(entry.Code, query)
            || Contains(entry.SongName, query)
            || Contains(entry.Artist, query)
            || Contains(entry.UploaderName, query)
            || Contains(entry.AudioUrl, query);
    }

    private static bool Contains(string? value, string query)
    {
        return value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? ExtractErrorMessage(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                return errorElement.GetString();
            }
        }
        catch
        {
            // Ignore invalid JSON bodies and fall back to the raw content.
        }

        return json.Trim();
    }
}
