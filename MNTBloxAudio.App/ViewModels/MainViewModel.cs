using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MNTBloxAudio.Core.Models;
using MNTBloxAudio.Core.Services;

namespace MNTBloxAudio.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore settingsStore = new();
    private readonly SongIndexService index = new();
    private readonly ReplacementSourceService sources = new();
    private readonly RobloxAssetDownloadService assets = new();
    private readonly RobloxAudioSessionService sessions = new();
    private readonly AutomaticCacheService cache = new();
    private readonly GitHubUpdateService updater = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim cacheGate = new(1, 1);
    private readonly Dictionary<ReplacementRule, (string Source, string Asset, PreparedCacheRule Prepared, DateTime LastWrite)> prepared = [];
    private readonly Dictionary<ReplacementRule, DateTimeOffset> retryAt = [];
    private AppSettings settings = new();
    private Task? monitorTask;
    private Task? updateTask;
    private Task? preparationTask;
    private bool initialized;
    private DateTimeOffset lastAudioActivity = DateTimeOffset.UtcNow;

    public ObservableCollection<ReplacementRule> Rules { get; } = [];
    [ObservableProperty] private ObservableCollection<SongIndexEntry> songSearchResults = [];
    [ObservableProperty] private SongIndexEntry? selectedSong;
    [ObservableProperty] private ReplacementRule? selectedRule;
    [ObservableProperty] private string songSearchQuery = "";
    [ObservableProperty] private string storedQuery = "";
    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private string searchStatus = "Find a song by name, artist, Roblox ID, or song code.";
    [ObservableProperty] private string status = "Starting your library…";
    [ObservableProperty] private string monitorStatus = "Watching for Roblox";
    [ObservableProperty] private string updateStatus = "Checking for updates…";
    [ObservableProperty] private bool updateReady;
    [ObservableProperty] private Uri? previewAudioSource;
    [ObservableProperty] private string targetAssetId = "";
    [ObservableProperty] private string editorAssetId = "";
    [ObservableProperty] private string editorSource = "";
    [ObservableProperty] private string editorName = "";
    [ObservableProperty] private string deviceId = "";
    public string VersionLabel => $"v{GitHubUpdateService.CurrentVersion.ToString(3)}";
    public bool HasSelectedSong => SelectedSong is not null;
    public bool HasSelectedRule => SelectedRule is not null;
    public bool SelectedSongStored => FindStoredSong() is not null;
    public bool NeedsTargetId => SelectedSong is not null && string.IsNullOrWhiteSpace(SelectedSong.LinkedAssetId);
    public string SongToggleLabel => FindStoredSong()?.IsEnabled == true ? "Disable" : "Enable replacement";
    public string RuleToggleLabel => SelectedRule?.IsEnabled == true ? "Disable" : "Enable replacement";
    public bool LibraryEmpty => Rules.Count == 0;
    public ICollectionView VisibleRules { get; }
    public MainViewModel()
    {
        VisibleRules = CollectionViewSource.GetDefaultView(Rules);
        VisibleRules.Filter = item => item is ReplacementRule rule && (string.IsNullOrWhiteSpace(StoredQuery)
            || $"{rule.Name} {rule.AssetIdPattern} {rule.FilePath}".Contains(StoredQuery.Trim(), StringComparison.OrdinalIgnoreCase));
    }


    public async Task InitializeAsync()
    {
        settings = await settingsStore.LoadAsync();
        DeviceId = DeviceIdentityService.GetOrCreate(settings.DeviceId);
        settings.DeviceId = DeviceId;
        foreach (var rule in settings.Rules)
        {
            Rules.Add(rule);
            rule.PropertyChanged += RuleChanged;
        }
        initialized = true;
        await SaveAsync();
        OpenIndexCommand.NotifyCanExecuteChanged();
        CopyDeviceIdCommand.NotifyCanExecuteChanged();
        NotifyLibrary();
        Status = string.Join(" ", new[] { settingsStore.RecoveryNotice, cache.RecoveryNotice }.Where(notice => !string.IsNullOrEmpty(notice)));
        if (string.IsNullOrEmpty(Status)) Status = "Store a song for later, or enable it to replace audio automatically.";
        monitorTask = MonitorAsync(lifetime.Token);
        updateTask = CheckForUpdatesAsync();
    }

    [RelayCommand]
    private async Task SearchSongsAsync()
    {
        try
        {
            SearchStatus = "Searching…";
            SelectedSong = null;
            var results = await index.SearchSongsAsync(SongSearchQuery, settings.SongIndexBaseUrl, lifetime.Token);
            SongSearchResults = new(results);
            SearchStatus = results.Count == 0 ? "No matches. Try another name, artist, ID, or code." : $"{results.Count} songs · Select one to preview, store, or enable.";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { SearchStatus = "Couldn't reach the index. Check your connection and search again. Your stored audio is still available."; }
    }

    private ReplacementRule? FindStoredSong() => SelectedSong is null ? null : Rules.FirstOrDefault(rule =>
        string.Equals(rule.SongCode, SelectedSong.Code, StringComparison.OrdinalIgnoreCase)
        || string.Equals(rule.FilePath, SelectedSong.Code, StringComparison.OrdinalIgnoreCase)
        || rule.FilePath == SelectedSong.AudioUrl && rule.AssetIdPattern == SelectedSong.LinkedAssetId);

    private ReplacementRule? StoreSong()
    {
        if (SelectedSong is null) return null;
        var existing = FindStoredSong();
        if (existing is not null) return existing;
        var rule = new ReplacementRule
        {
            Name = SelectedSong.SongName,
            SongCode = SelectedSong.Code,
            FilePath = SelectedSong.AudioUrl,
            AssetIdPattern = SelectedSong.LinkedAssetId,
            IsEnabled = false,
        };
        Rules.Add(rule);
        rule.PropertyChanged += RuleChanged;
        NotifyLibrary();
        return rule;
    }

    [RelayCommand]
    private async Task StoreSelectedSongAsync()
    {
        if (StoreSong() is not null) { await SaveAsync(); Status = "Stored. Enable it whenever you're ready."; }
    }

    [RelayCommand]
    private async Task ToggleSelectedSongAsync()
    {
        var rule = StoreSong();
        if (rule is null) return;
        if (string.IsNullOrWhiteSpace(rule.AssetIdPattern)) rule.AssetIdPattern = TargetAssetId.Trim();
        await ToggleAsync(rule);
    }

    [RelayCommand]
    private async Task ToggleSelectedRuleAsync()
    {
        if (SelectedRule is not null) await ToggleAsync(SelectedRule);
    }

    private async Task ToggleAsync(ReplacementRule rule)
    {
        await cacheGate.WaitAsync();
        try
        {
        if (!rule.IsEnabled && (!ValidAssetId(rule.AssetIdPattern) || string.IsNullOrWhiteSpace(rule.FilePath)))
        {
            Status = "Add the Roblox sound ID and a replacement source first.";
            SelectedRule = rule;
            SelectedTab = 1;
            return;
        }
        rule.IsEnabled = !rule.IsEnabled;
        // Only one replacement can own a Roblox asset at a time.
        if (rule.IsEnabled)
            foreach (var other in Rules.Where(other => other != rule && other.AssetIdPattern == rule.AssetIdPattern)) other.IsEnabled = false;
        retryAt.Remove(rule);
        await SaveAsync();
        Status = rule.IsEnabled ? "Enabled. Audio will be prepared and applied automatically when cached." : "Disabled. Restoring the original as soon as its cache file is free.";
        NotifyLibrary();
    
        }
        finally { cacheGate.Release(); }
    }

    [RelayCommand]
    private async Task RemoveSelectedSongAsync()
    {
        if (FindStoredSong() is { } rule) await RemoveAsync(rule);
    }

    [RelayCommand]
    private async Task RemoveSelectedRuleAsync()
    {
        if (SelectedRule is { } rule) await RemoveAsync(rule);
    }

    private async Task RemoveAsync(ReplacementRule rule)
    {
        await cacheGate.WaitAsync();
        try
        {
        rule.IsEnabled = false;
        rule.PropertyChanged -= RuleChanged;
        Rules.Remove(rule);
        prepared.Remove(rule);
        retryAt.Remove(rule);
        if (SelectedRule == rule) SelectedRule = null;
        await SaveAsync();
        NotifyLibrary();
        Status = "Removed from Stored. Any replaced cache files remain queued for safe restoration.";
    
        }
        finally { cacheGate.Release(); }
    }

    [RelayCommand]
    private void AddLocalAudio()
    {
        var dialog = new OpenFileDialog { Filter = "Audio|*.mp3;*.wav;*.ogg;*.m4a;*.aac;*.flac;*.wma|All files|*.*", Title = "Choose replacement audio" };
        if (dialog.ShowDialog() != true) return;
        var rule = new ReplacementRule { Name = Path.GetFileNameWithoutExtension(dialog.FileName), FilePath = dialog.FileName, IsEnabled = false };
        Rules.Add(rule);
        rule.PropertyChanged += RuleChanged;
        SelectedRule = rule;
        SelectedTab = 1;
        NotifyLibrary();
        Status = "Add the Roblox sound ID, then save and enable.";
    }

    [RelayCommand]
    private async Task SaveDetailsAsync()
    {
        await cacheGate.WaitAsync();
        try
        {
        if (SelectedRule is not { } rule) return;
        if (!ValidAssetId(EditorAssetId) || string.IsNullOrWhiteSpace(EditorSource)) { Status = "Enter a Roblox sound ID (digits only) and an audio file, URL, or code."; return; }
        rule.IsEnabled = false;
        rule.Name = string.IsNullOrWhiteSpace(EditorName) ? "My audio" : EditorName.Trim();
        rule.AssetIdPattern = EditorAssetId.Trim();
        if (rule.FilePath != EditorSource.Trim()) rule.SongCode = "";
        rule.FilePath = EditorSource.Trim();
        rule.SourceAssetHash = ""; rule.ReplacementFileHash = "";
        prepared.Remove(rule);
        retryAt.Remove(rule);
        await SaveAsync();
        Status = "Saved. Enable the replacement when you're ready.";
    
        }
        finally { cacheGate.Release(); }
    }

    [RelayCommand]
    private async Task PreviewSongAsync()
    {
        if (SelectedSong is null) return;
        PreviewAudioSource = null;
        if (Uri.TryCreate(SelectedSong.AudioUrl, UriKind.Absolute, out var uri)) PreviewAudioSource = uri;
        await Task.CompletedTask;
    }
    [RelayCommand] private void StopPreview() => PreviewAudioSource = null;
    private bool CanUseDeviceId() => initialized && !string.IsNullOrWhiteSpace(DeviceId);
    [RelayCommand(CanExecute = nameof(CanUseDeviceId))]
    private void OpenIndex()
    {
        try
        {
            var uri = DeviceIdentityService.BuildUploadUri(index.GetSiteBaseUrl(settings.SongIndexBaseUrl), DeviceId);
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception) { Status = "Couldn't open your browser. Copy your device ID and visit mntbloxindex.vercel.app/upload.html."; }
    }
    [RelayCommand(CanExecute = nameof(CanUseDeviceId))]
    private void CopyDeviceId()
    {
        try { Clipboard.SetText(DeviceId); Status = "Device ID copied. The upload page also remembers it for your next visit."; }
        catch (Exception) { Status = "Clipboard is busy. Try copying the device ID again."; }
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(750));
        do
        {
            try { await ReconcileAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception exception) { MonitorStatus = $"Retrying automatically: {exception.Message}"; }
        } while (await timer.WaitForNextTickAsync(token));
    }

    private async Task ReconcileAsync(CancellationToken token)
    {
        await SynchronizeAsync(token);
        IReadOnlyList<RobloxAudioSessionInfo> currentSessions;
        try { currentSessions = await Task.Run(sessions.GetRobloxSessions, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { currentSessions = []; }
        // Peak alone cannot identify an individual Roblox asset. Be conservative while any audio is audible.
        if (currentSessions.Any(session => session.PeakMeter > 0.001f)) lastAudioActivity = DateTimeOffset.UtcNow;
        var busy = currentSessions.Count > 0 && DateTimeOffset.UtcNow - lastAudioActivity < TimeSpan.FromSeconds(1.5);
        MonitorStatus = currentSessions.Count == 0 ? "Watching for Roblox" : busy ? "Roblox audio is playing" : "Roblox connected · watching cache";

        // Preparation stays independent of cache restoration.
        if (preparationTask is null || preparationTask.IsCompleted)
            preparationTask = PrepareEnabledAsync(token);
    }

    private async Task PrepareEnabledAsync(CancellationToken token)
    {
        foreach (var rule in Rules.Where(rule => rule.IsEnabled).ToArray())
        {
            if (prepared.TryGetValue(rule, out var ready) && ready.Source == rule.FilePath && ready.Asset == rule.AssetIdPattern && File.Exists(ready.Prepared.LocalPath) && File.GetLastWriteTimeUtc(ready.Prepared.LocalPath) == ready.LastWrite) continue;
            if (retryAt.TryGetValue(rule, out var retry) && retry > DateTimeOffset.UtcNow) continue;
            var sourceReference = rule.FilePath;
            var assetId = rule.AssetIdPattern;
            try
            {
                rule.AutomationStatus = "Preparing audio…";
                if (!ValidAssetId(assetId)) throw new InvalidOperationException("Add a valid Roblox sound ID.");
                var reference = sourceReference;
                if (SongIndexService.LooksLikeSongCode(reference))
                    reference = (await index.ResolveSongCodeAsync(reference, settings.SongIndexBaseUrl, token))?.AudioUrl ?? throw new InvalidOperationException("Song code not found.");
                var resolved = await sources.ResolveAsync(reference, cancellationToken: token) ?? throw new InvalidOperationException("Replacement source is unavailable.");
                var original = await assets.DownloadAssetInfoAsync(assetId, token);
                var hash = await Task.Run(() => { using var file = File.OpenRead(resolved.LocalPath); return Convert.ToHexString(SHA256.HashData(file)); }, token);
                if (!Rules.Contains(rule) || sourceReference != rule.FilePath || assetId != rule.AssetIdPattern) continue;
                rule.SourceAssetHash = original.Sha256;
                rule.ReplacementFileHash = hash;
                rule.SourceAssetLength = original.Length;
                rule.ReplacementFileLength = resolved.Length;
                rule.PreparationVersion = ReplacementRule.LatestPreparationVersion;
                rule.PreparedAt = DateTimeOffset.Now;
                prepared[rule] = (sourceReference, assetId, new(assetId, original.Sha256, hash, resolved.LocalPath), File.GetLastWriteTimeUtc(resolved.LocalPath));
                await SaveAsync();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                rule.AutomationStatus = $"Will retry · {exception.Message}";
                retryAt[rule] = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
    }

    private async Task SynchronizeAsync(CancellationToken token)
    {
        await cacheGate.WaitAsync(token);
        try
        {
            var known = Rules.Where(rule => !string.IsNullOrEmpty(rule.SourceAssetHash) && !string.IsNullOrEmpty(rule.ReplacementFileHash))
                .Select(rule => new PreparedCacheRule(rule.AssetIdPattern, rule.SourceAssetHash, rule.ReplacementFileHash, "")).ToArray();
            var desired = Rules.Where(rule => rule.IsEnabled).Select(rule =>
                prepared.TryGetValue(rule, out var ready) && ready.Source == rule.FilePath && ready.Asset == rule.AssetIdPattern
                    ? ready.Prepared : new PreparedCacheRule(rule.AssetIdPattern, rule.SourceAssetHash, rule.ReplacementFileHash, "")).ToArray();
            var result = await Task.Run(() => { cache.ImportLegacyBackups(known); return cache.Synchronize(desired); }, token);
            if (result.Errors.TryGetValue("*", out var recoveryError)) Status = recoveryError;
            foreach (var rule in Rules)
            {
                if (result.Errors.TryGetValue("*", out var globalError)) rule.AutomationStatus = globalError;
                else if (result.Errors.TryGetValue(rule.AssetIdPattern, out var error)) rule.AutomationStatus = $"{(rule.IsEnabled ? "Replacement" : "Restore")} failed - {error}";
                else if (!rule.IsEnabled) rule.AutomationStatus = result.PendingAssets.Contains(rule.AssetIdPattern) ? "Waiting for the cache file to be released - retrying automatically" : "Stored - disabled";
                else if (result.AppliedAssets.Contains(rule.AssetIdPattern)) rule.AutomationStatus = "Enabled - replaced";
                else if (prepared.ContainsKey(rule)) rule.AutomationStatus = "Enabled - waiting for cached audio";
            }
        }
        finally { cacheGate.Release(); }
    }

    private async Task SaveAsync()
    {
        settings.Rules = Rules.ToList();
        settings.AutoApplyCacheReplacements = true;
        await settingsStore.SaveAsync(settings);
    }

    private void RuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReplacementRule.IsEnabled) or nameof(ReplacementRule.Name)) NotifyLibrary();
    }
    private void NotifyLibrary()
    {
        OnPropertyChanged(nameof(LibraryEmpty));
        OnPropertyChanged(nameof(SelectedSongStored)); OnPropertyChanged(nameof(SongToggleLabel)); OnPropertyChanged(nameof(RuleToggleLabel));
    }
    partial void OnStoredQueryChanged(string value) => VisibleRules.Refresh();
    partial void OnSelectedSongChanged(SongIndexEntry? value)
    {
        TargetAssetId = value?.LinkedAssetId ?? "";
        OnPropertyChanged(nameof(HasSelectedSong)); OnPropertyChanged(nameof(NeedsTargetId)); NotifyLibrary();
    }
    partial void OnSelectedRuleChanged(ReplacementRule? value)
    {
        EditorAssetId = value?.AssetIdPattern ?? ""; EditorSource = value?.FilePath ?? ""; EditorName = value?.Name ?? "";
        OnPropertyChanged(nameof(HasSelectedRule)); NotifyLibrary();
    }
    private static bool ValidAssetId(string value) => value.Length > 0 && value.All(c => c is >= '0' and <= '9');

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateStatus = "Checking for updates…";
            UpdateReady = await updater.StageLatestAsync(lifetime.Token);
            UpdateStatus = UpdateReady ? "Update ready · restart to install" : "You're up to date";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { UpdateStatus = "Update check unavailable · click to retry"; }
    }
    [RelayCommand]
    private void RestartForUpdate()
    {
        try { updater.InstallOnExit(); Application.Current.MainWindow.Close(); }
        catch (Exception exception) { UpdateStatus = $"Couldn't install update: {exception.Message}"; }
    }

    public async Task ShutdownAsync()
    {
        if (!initialized) return;
        lifetime.Cancel();
        try { if (monitorTask is not null) await monitorTask; } catch (OperationCanceledException) { }
        try { if (preparationTask is not null) await preparationTask; } catch (OperationCanceledException) { }
        try { if (updateTask is not null) await updateTask; } catch (OperationCanceledException) { }
        await SaveAsync();
    }
}
