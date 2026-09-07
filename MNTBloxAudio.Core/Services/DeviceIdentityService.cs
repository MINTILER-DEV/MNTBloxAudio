namespace MNTBloxAudio.Core.Services;

public static class DeviceIdentityService
{
    public static string GetOrCreate(string? savedId) => string.IsNullOrWhiteSpace(savedId)
        ? Guid.NewGuid().ToString("N") : savedId.Trim();

    public static Uri BuildUploadUri(string siteBaseUrl, string deviceId)
    {
        var builder = new UriBuilder(new Uri(new Uri(siteBaseUrl), "upload.html"))
        {
            // A fragment stays in the browser instead of being sent in the page request.
            Fragment = "deviceId=" + Uri.EscapeDataString(deviceId),
        };
        return builder.Uri;
    }
}
