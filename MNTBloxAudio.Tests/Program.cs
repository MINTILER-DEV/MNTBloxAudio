using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MNTBloxAudio.App;
using MNTBloxAudio.App.ViewModels;
using MNTBloxAudio.Core.Models;
using MNTBloxAudio.Core.Services;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    [STAThread]
    public static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "MNTBloxAudio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cachePath = Path.Combine(root, "cache"); Directory.CreateDirectory(cachePath);
            var statePath = Path.Combine(root, "state");
            var path = Path.Combine(cachePath, "RBXone");
            var other = Path.Combine(cachePath, "RBXtwo");
            var source = Path.Combine(root, "replacement.mp3"); File.WriteAllText(source, "replacement");
            var first = new PreparedCacheRule("1", Hash("original-one"), Hash("replacement"), source);
            var second = new PreparedCacheRule("2", Hash("original-two"), Hash("replacement"), source);
            var engine = new AutomaticCacheService(cachePath, statePath);
            File.WriteAllText(path, "original-one"); File.WriteAllText(other, "original-two");
            engine.Synchronize([first, second]);
            Check(File.ReadAllText(path) == "replacement" && File.ReadAllText(other) == "replacement", "Auto apply both exact originals");
            var result = engine.Synchronize([second]);
            Check(!result.PendingAssets.Contains("1") && File.ReadAllText(path) == "original-one", "Restore a free cache file even while unrelated Roblox audio plays");
            engine.Synchronize([first, second]);
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                for (var i = 0; i < 40; i++) Check(engine.Synchronize([second]).PendingAssets.Contains("1"), "Retry locked file indefinitely");
            engine = new AutomaticCacheService(cachePath, statePath);
            engine.Synchronize([second]);
            Check(File.ReadAllText(path) == "original-one", "Restore after restart and release");
            Check(File.ReadAllText(other) == "replacement", "Identical replacements retain separate owners");
            engine.Synchronize([first, second]);
            File.WriteAllText(path, "unrelated Roblox content");
            engine.Synchronize([second]);
            Check(File.ReadAllText(path) == "unrelated Roblox content", "Do not overwrite reused cache filename");
            File.Delete(path); engine.Synchronize([first, second]);
            File.WriteAllText(path, "original-one"); engine.Synchronize([first, second]);
            Check(File.ReadAllText(path) == "replacement", "Reapply after cache eviction without disabling rule");
            engine.Synchronize([]);
            Check(File.ReadAllText(path) == "original-one" && File.ReadAllText(other) == "original-two", "Removing all rules restores exact originals");
            File.WriteAllText(source, "tampered"); engine.Synchronize([first]);
            Check(File.ReadAllText(path) == "original-one", "Reject changed source before writing");
            File.WriteAllText(source, "replacement");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                engine.Synchronize([first]);
            Check(File.ReadAllText(path) == "original-one", "Never replace an open cache file");
            engine.Synchronize([first]);
            Check(File.ReadAllText(path) == "replacement", "Apply when original file is released");
            var legacyPath = Path.Combine(cachePath, "RBXlegacy"); File.WriteAllText(legacyPath, "replacement");
            File.WriteAllText(Path.Combine(statePath, "sound-cache-backups", "RBXlegacy.bak"), "original-two");
            engine.ImportLegacyBackups([second]); engine.Synchronize([]);
            Check(File.ReadAllText(legacyPath) == "original-two", "Migrate old backups for disabled rules");
            Check(GitHubUpdateService.TryParseStableVersion("v1.3.1", out var v) && v == new Version(1, 3, 1), "Compare stable release versions");
            Check(!GitHubUpdateService.TryParseStableVersion("v1.4.0-beta", out _), "Reject prerelease tags");
            Check(!GitHubUpdateService.TryParseStableVersion("unknown", out _), "Reject malformed tags");
            var existingDeviceId = "MNT_existing-123";
            Check(DeviceIdentityService.GetOrCreate(existingDeviceId) == existingDeviceId, "Keep the existing device identity");
            var generatedDeviceId = DeviceIdentityService.GetOrCreate("");
            Check(Guid.TryParseExact(generatedDeviceId, "N", out _), "Generate an identity for new app installs");
            var uploadUri = DeviceIdentityService.BuildUploadUri("https://mntbloxindex.vercel.app/", existingDeviceId);
            Check(uploadUri.AbsolutePath == "/upload.html" && uploadUri.Query == "", "Open upload without putting device identity in server query logs");
            Check(uploadUri.Fragment == "#deviceId=MNT_existing-123", "Pass the saved app identity to the browser fragment");
            TestRestoreFailures(root);
            TestUpdater(root).GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {assertions} cache recovery and update assertions");
            RenderUi();
        }
        finally { Directory.Delete(root, true); }
    }
    private static void TestRestoreFailures(string root)
    {
        var cachePath = Path.Combine(root, "restore-failures"); Directory.CreateDirectory(cachePath);
        var statePath = Path.Combine(root, "restore-state");
        var path = Path.Combine(cachePath, "RBXrestore");
        var source = Path.Combine(root, "restore-source.mp3");
        File.WriteAllText(path, "original"); File.WriteAllText(source, "replacement");
        var rule = new PreparedCacheRule("restore-test", Hash("original"), Hash("replacement"), source);
        var engine = new AutomaticCacheService(cachePath, statePath);
        engine.Synchronize([rule]);
        var backup = Path.Combine(statePath, "sound-cache-backups", Hash("original") + ".original");
        File.Delete(backup);
        var result = engine.Synchronize([]);
        Check(result.Errors.ContainsKey(rule.AssetId) && !result.PendingAssets.Contains(rule.AssetId), "Missing backup is an error, not a busy-file wait");
        Check(File.ReadAllText(path) == "replacement", "Missing backup does not destroy cached audio");
        File.WriteAllText(backup, "corrupt");
        result = engine.Synchronize([]);
        Check(result.Errors.ContainsKey(rule.AssetId) && !result.PendingAssets.Contains(rule.AssetId), "Corrupt backup is an error, not a busy-file wait");
        Check(File.ReadAllText(path) == "replacement", "Corrupt backup is rejected before touching audio");
        File.WriteAllText(backup, "original");
        result = engine.Synchronize([]);
        Check(result.Errors.Count == 0 && File.ReadAllText(path) == "original", "Restoration recovers when a valid backup returns");
        engine.Synchronize([rule]);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            result = engine.Synchronize([]);
            Check(result.Errors.ContainsKey(rule.AssetId) && !result.PendingAssets.Contains(rule.AssetId), "Permission failure is not mislabeled as audio playing");
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        result = engine.Synchronize([]);
        Check(result.Errors.Count == 0 && File.ReadAllText(path) == "original", "Restoration retries after permissions recover");
        engine.Synchronize([rule]);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            result = engine.Synchronize([]);
            Check(result.PendingAssets.Contains(rule.AssetId) && result.Errors.Count == 0, "An actual open handle remains queued without errors");
        }
        result = engine.Synchronize([]);
        Check(result.PendingAssets.Count == 0 && File.ReadAllText(path) == "original", "Restore on the first pass after handle release");
    }
    private sealed class ReleaseHandler(string tag, string digest, bool prerelease = false, string url = "https://github.com/MINTILER-DEV/MNTBloxAudio/releases/download/v999.0.0/MNTBloxAudio.exe") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host == "api.github.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = tag, draft = false, prerelease, assets = new[] { new { name = "MNTBloxAudio.exe", browser_download_url = url, digest, size = 11 } }
                })) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("replacement") });
        }
    }
    private static async Task TestUpdater(string root)
    {
        using var good = new HttpClient(new ReleaseHandler("v999.0.0", "sha256:" + Hash("replacement")));
        Check(await new GitHubUpdateService(good, Path.Combine(root, "updates")).StageLatestAsync(default), "Stage newer verified release");
        using var old = new HttpClient(new ReleaseHandler("v1.0.0", "sha256:" + Hash("replacement")));
        Check(!await new GitHubUpdateService(old, root).StageLatestAsync(default), "Never downgrade");
        using var preview = new HttpClient(new ReleaseHandler("v999.0.0", "sha256:" + Hash("replacement"), true));
        Check(!await new GitHubUpdateService(preview, root).StageLatestAsync(default), "Skip prerelease metadata");
        foreach (var digest in new[] { "", "sha256:" + Hash("tampered") })
        {
            using var bad = new HttpClient(new ReleaseHandler("v999.0.0", digest));
            try { await new GitHubUpdateService(bad, root).StageLatestAsync(default); throw new Exception("Unverified update accepted"); }
            catch (InvalidDataException) { assertions++; }
        }
        using var wrongRepo = new HttpClient(new ReleaseHandler("v999.0.0", "sha256:" + Hash("replacement"), url: "https://example.test/MNTBloxAudio.exe"));
        try { await new GitHubUpdateService(wrongRepo, root).StageLatestAsync(default); throw new Exception("Foreign update accepted"); }
        catch (InvalidDataException) { assertions++; }
    }
    private static void RenderUi()
    {
        // Render the real WPF templates without initializing network, monitoring, or user settings.
        var app = new App();
        app.InitializeComponent();
        var vm = new MainViewModel();
        var window = new MainWindow(vm) { Width = 1120, Height = 820 };
        var grid = (FrameworkElement)window.Content;
        var output = Path.GetFullPath("artifacts/ui"); Directory.CreateDirectory(output);
        void Render(string name)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            grid.Measure(new Size(1056, 730)); grid.Arrange(new Rect(0, 0, 1056, 730)); grid.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1056, 730, 96, 96, PixelFormats.Pbgra32); bitmap.Render(grid);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        Render("search-empty");
        var song = new SongIndexEntry { Code = "ABCDEF", SongName = "Night Drive", Artist = "Example artist", LinkedAssetId = "123456789", AudioUrl = "https://example.test/audio.mp3" };
        vm.SongSearchResults.Add(song); vm.SelectedSong = song;
        Render("search-selected");
        var rule = new ReplacementRule { Name = "Night Drive", AssetIdPattern = "123456789", FilePath = "C:/Music/Night Drive.mp3", AutomationStatus = "Enabled - waiting for cached audio" };
        vm.Rules.Add(rule); vm.SelectedRule = rule; vm.SelectedTab = 1;
        Render("stored-selected");
        Console.WriteLine("PASS: rendered Search, selected result, and Stored WPF layouts");
    }
}
