using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MNTBloxAudio.App;

public sealed class GitHubUpdateService
{
    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 3, 0);
    private const string Repository = "MINTILER-DEV/MNTBloxAudio";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private string? stagedPath;
    private string? stagedHash;

    public async Task<bool> StageLatestAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd($"MNTBloxAudio/{CurrentVersion.ToString(3)}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return false;
        if (!TryParseStableVersion(release.GetProperty("tag_name").GetString(), out var latest) || latest <= CurrentVersion) return false;
        var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(asset => asset.GetProperty("name").GetString() == "MNTBloxAudio.exe");
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Release has no Windows executable.");
        var url = asset.GetProperty("browser_download_url").GetString()!;
        if (!url.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected update source.");
        var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:") || digest.Length != 71) throw new InvalidDataException("Release has no SHA-256 digest.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MNTBloxAudio", "updates", latest.ToString());
        Directory.CreateDirectory(directory);
        var downloadPath = Path.Combine(directory, "MNTBloxAudio.exe.download");
        using var download = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        download.EnsureSuccessStatusCode();
        await using (var destination = File.Create(downloadPath))
        {
            await using var stream = await download.Content.ReadAsStreamAsync(token);
            await stream.CopyToAsync(destination, token);
        }
        await using (var file = File.OpenRead(downloadPath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase) || file.Length != asset.GetProperty("size").GetInt64())
                throw new InvalidDataException("Update verification failed.");
        }
        stagedPath = Path.Combine(directory, "MNTBloxAudio.exe");
        File.Move(downloadPath, stagedPath, true);
        stagedHash = digest[7..];
        return true;
    }

    public static bool TryParseStableVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        return tag is not null && Version.TryParse(tag.TrimStart('v', 'V'), out version!);
    }

    public void InstallOnExit()
    {
        if (stagedPath is null || stagedHash is null) throw new InvalidOperationException("No verified update is ready.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate this application.");
        if (!Path.GetFileName(executable).Equals("MNTBloxAudio.exe", StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "MNTBloxAudio.dll")))
            throw new InvalidOperationException("Run the published MNTBloxAudio.exe to install updates.");
        var arguments = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            processId = Environment.ProcessId, source = stagedPath, target = executable, hash = stagedHash,
        })));
        var script = """
            $ErrorActionPreference = 'Stop'
            $update = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__DATA__')) | ConvertFrom-Json
            $log = Join-Path (Split-Path -LiteralPath $update.source) 'install.log'
            $backup = $update.target + '.previous'
            $incoming = $update.target + '.update'
            $installed = $false
            try {
                if ((Get-FileHash -LiteralPath $update.source -Algorithm SHA256).Hash -ne $update.hash) { throw 'Update checksum changed.' }
                $running = Get-Process -Id $update.processId -ErrorAction SilentlyContinue
                if ($running) { if (-not $running.WaitForExit(120000)) { throw 'Application did not exit.' } }
                Copy-Item -LiteralPath $update.source -Destination $incoming -Force
                [IO.File]::Replace($incoming, $update.target, $backup)
                $installed = $true
                Start-Process -FilePath $update.target -WorkingDirectory (Split-Path -LiteralPath $update.target)
            } catch {
                $_ | Out-File -LiteralPath $log
                if ($installed -and (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $backup -Destination $update.target -Force }
            }
            """.Replace("__DATA__", arguments, StringComparison.Ordinal);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
            UseShellExecute = false, CreateNoWindow = true,
        });
    }
}
