using System.Net;
using MNTBloxAudio.Core.Models;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace MNTBloxAudio.Core.Services;

public sealed class ProxyFallbackService : IDisposable
{
    private readonly object syncRoot = new();
    private List<ReplacementRule> rules = [];
    private ProxyServer? proxyServer;
    private ExplicitProxyEndPoint? endPoint;

    public event EventHandler<ProxyAssetDetectedEventArgs>? AssetDetected;
    public event EventHandler<ProxyRequestObservedEventArgs>? RequestObserved;

    public ProxyStatusInfo Status { get; private set; } = new();

    public bool IsRunning => Status.IsRunning;

    public void UpdateRules(IEnumerable<ReplacementRule> replacementRules)
    {
        lock (syncRoot)
        {
            rules = replacementRules
                .Select(rule => new ReplacementRule
                {
                    Name = rule.Name,
                    AssetIdPattern = rule.AssetIdPattern,
                    FilePath = rule.FilePath,
                    IsEnabled = rule.IsEnabled,
                    GainPercent = rule.GainPercent,
                })
                .ToList();
        }
    }

    public Task<ProxyStatusInfo> StartAsync(int port, bool replaceResponses)
    {
        if (IsRunning)
        {
            return Task.FromResult(Status);
        }

        proxyServer = new ProxyServer();
        proxyServer.BeforeRequest += OnBeforeRequestAsync;

        try
        {
            var certificateManager = proxyServer.CertificateManager;
            var certificateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MNTBloxAudio",
                "proxy");
            var pfxPath = Path.Combine(certificateDirectory, "rootCert.pfx");

            Directory.CreateDirectory(certificateDirectory);

            if (File.Exists(pfxPath))
            {
                var pfxFileInfo = new FileInfo(pfxPath);
                if (pfxFileInfo.Length == 0)
                {
                    pfxFileInfo.Delete();
                }
            }

            certificateManager.RootCertificateName = "MNTBloxAudio Proxy Root";
            certificateManager.RootCertificateIssuerName = "MNTBloxAudio";
            certificateManager.PfxPassword = "mntbloxaudio-local-proxy";
            certificateManager.PfxFilePath = pfxPath;

            // Use current-user trust only; `true` on TrustRootCertificate means machine store.
            certificateManager.EnsureRootCertificate(userTrustRootCertificate: true, machineTrustRootCertificate: false, trustRootCertificateAsAdmin: false);
        }
        catch (Exception exception)
        {
            proxyServer.BeforeRequest -= OnBeforeRequestAsync;
            proxyServer.Dispose();
            proxyServer = null;

            throw new InvalidOperationException(
                "Could not create or trust the proxy certificate for the current user. Run the app normally, ensure your user certificate store is writable, or keep proxy fallback disabled.",
                exception);
        }

        endPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, port, true);
        endPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequestAsync;

        proxyServer.AddEndPoint(endPoint);
        proxyServer.Start();
        proxyServer.SetAsSystemHttpProxy(endPoint);
        proxyServer.SetAsSystemHttpsProxy(endPoint);

        Status = new ProxyStatusInfo
        {
            IsRunning = true,
            Port = port,
            StatusText = replaceResponses
                ? $"Proxy is running on 127.0.0.1:{port} with response replacement enabled."
                : $"Proxy is running on 127.0.0.1:{port} in detection-only mode.",
        };

        return Task.FromResult(Status);
    }

    public Task<ProxyStatusInfo> StopAsync()
    {
        if (proxyServer is null)
        {
            Status = new ProxyStatusInfo();
            return Task.FromResult(Status);
        }

        if (endPoint is not null)
        {
            endPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequestAsync;
        }

        proxyServer.BeforeRequest -= OnBeforeRequestAsync;

        try
        {
            proxyServer.RestoreOriginalProxySettings();
        }
        catch
        {
            // Best effort only.
        }

        proxyServer.Stop();
        proxyServer.Dispose();
        proxyServer = null;
        endPoint = null;

        Status = new ProxyStatusInfo
        {
            IsRunning = false,
            Port = 0,
            StatusText = "Proxy is disabled.",
        };

        return Task.FromResult(Status);
    }

    public void Dispose()
    {
        _ = StopAsync();
    }

    private Task OnBeforeTunnelConnectRequestAsync(object sender, TunnelConnectSessionEventArgs e)
    {
        var host = e.HttpClient.Request.RequestUri.Host;

        e.DecryptSsl = host.EndsWith("roblox.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("rbxcdn.com", StringComparison.OrdinalIgnoreCase);

        return Task.CompletedTask;
    }

    private Task OnBeforeRequestAsync(object sender, SessionEventArgs e)
    {
        var requestUri = e.HttpClient.Request.RequestUri;
        var host = requestUri.Host;
        if (!IsRobloxProxyHost(host))
        {
            return Task.CompletedTask;
        }

        var isAssetDeliveryRequest = IsAssetDeliveryRequest(requestUri);
        if (!isAssetDeliveryRequest)
        {
            RequestObserved?.Invoke(this, new ProxyRequestObservedEventArgs
            {
                Host = host,
                Url = requestUri.ToString(),
                IsAssetDeliveryRequest = false,
            });

            return Task.CompletedTask;
        }

        var assetId = TryGetAssetId(requestUri);
        var matchedRule = string.IsNullOrWhiteSpace(assetId)
            ? null
            : FindMatchingRule(assetId);
        var replacedResponse = false;

        if (!string.IsNullOrWhiteSpace(assetId)
            && matchedRule is not null
            && matchedRule.IsEnabled
            && File.Exists(matchedRule.FilePath))
        {
            try
            {
                var bytes = File.ReadAllBytes(matchedRule.FilePath);
                var headers = new List<HttpHeader>
                {
                    new("Content-Type", GuessContentType(matchedRule.FilePath)),
                    new("Content-Length", bytes.Length.ToString()),
                    new("Cache-Control", "no-store"),
                };

                e.Ok(bytes, headers, true);
                replacedResponse = true;
            }
            catch
            {
                // If replacement fails, detection still flows to the UI.
            }
        }

        RequestObserved?.Invoke(this, new ProxyRequestObservedEventArgs
        {
            Host = host,
            Url = requestUri.ToString(),
            IsAssetDeliveryRequest = true,
            AssetId = assetId,
            MatchedRuleName = matchedRule?.Name,
            ResponseWasReplaced = replacedResponse,
        });

        if (string.IsNullOrWhiteSpace(assetId))
        {
            return Task.CompletedTask;
        }

        AssetDetected?.Invoke(this, new ProxyAssetDetectedEventArgs
        {
            AssetId = assetId,
            Host = host,
            Url = requestUri.ToString(),
            MatchedRuleName = matchedRule?.Name,
            ResponseWasReplaced = replacedResponse,
        });

        return Task.CompletedTask;
    }

    private ReplacementRule? FindMatchingRule(string assetId)
    {
        lock (syncRoot)
        {
            return rules.FirstOrDefault(rule => RuleMatcher.Matches(rule.AssetIdPattern, assetId));
        }
    }

    private static string GuessContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            _ => "audio/mpeg",
        };
    }

    private static string? TryGetAssetId(Uri requestUri)
    {
        var assetIdFromPath = TryGetAssetIdFromPath(requestUri);
        if (!string.IsNullOrWhiteSpace(assetIdFromPath))
        {
            return assetIdFromPath;
        }

        var query = requestUri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], "id", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private static string? TryGetAssetIdFromPath(Uri requestUri)
    {
        var segments = requestUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "assetId", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(segments[index + 1]);
            }
        }

        return null;
    }

    private static bool IsRobloxProxyHost(string host)
    {
        return host.EndsWith("roblox.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("rbxcdn.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssetDeliveryRequest(Uri requestUri)
    {
        if (requestUri.Host.Contains("assetdelivery.roblox.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return requestUri.Host.Contains("apis.roblox.com", StringComparison.OrdinalIgnoreCase)
            && requestUri.AbsolutePath.Contains("/asset-delivery-api/", StringComparison.OrdinalIgnoreCase);
    }
}
