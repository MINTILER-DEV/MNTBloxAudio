using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MNTBloxAudio.Core.Models;
using MNTBloxAudio.Core.Services;

namespace MNTBloxAudio.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SemaphoreSlim playbackLock = new(1, 1);
    private readonly SettingsStore settingsStore;
    private readonly AudioDeviceService deviceService;
    private readonly RobloxAudioSessionService sessionService;
    private readonly ReplacementPlaybackService playbackService;
    private readonly ProxyFallbackService proxyService;
    private readonly RobloxPlayerLogService playerLogService;
    private readonly RobloxSoundCacheService soundCacheService;
    private readonly RobloxAssetDownloadService assetDownloadService;
    private readonly ReplacementSourceService replacementSourceService;
    private readonly SongIndexService songIndexService;
    private readonly Lock monitorStateLock = new();
    private readonly HashSet<ReplacementRule> observedCacheRules = [];

    private AppSettings settings = new();
    private bool initializationComplete;
    private bool robloxMuted;
    private string? selectedOutputDeviceId;
    private CancellationTokenSource? monitorCancellationTokenSource;
    private Task? monitorTask;
    private bool lastRobloxAudioActivityState;
    private HashSet<string> lastSessionIdentifiers = [];

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> renderDevices = [];

    [ObservableProperty]
    private ObservableCollection<RobloxAudioSessionInfo> robloxSessions = [];

    [ObservableProperty]
    private ObservableCollection<ReplacementRule> rules = [];

    [ObservableProperty]
    private ObservableCollection<ActivityLogEntry> activity = [];

    [ObservableProperty]
    private ObservableCollection<ActivityLogEntry> proxyOutput = [];

    [ObservableProperty]
    private ObservableCollection<RobloxSoundCacheEntry> cachedSoundFiles = [];

    [ObservableProperty]
    private ObservableCollection<UploadedSongRecord> uploadedSongs = [];

    [ObservableProperty]
    private ObservableCollection<SongIndexEntry> songSearchResults = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedOutputDeviceName))]
    private AudioDeviceInfo? selectedOutputDevice;

    [ObservableProperty]
    private ReplacementRule? selectedRule;

    [ObservableProperty]
    private UploadedSongRecord? selectedUploadedSong;

    [ObservableProperty]
    private SongIndexEntry? selectedSongSearchResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputVolumeLabel))]
    private int outputVolumePercent = 100;

    [ObservableProperty]
    private bool enableProxyFallback;

    [ObservableProperty]
    private bool autoReplaceOnRobloxAudioActivity;

    [ObservableProperty]
    private bool enableExperimentalProxyReplacement;

    [ObservableProperty]
    private bool autoReplaceOnDetection = true;

    [ObservableProperty]
    private bool autoMuteRobloxDuringPlayback = true;

    [ObservableProperty]
    private bool autoRestoreRobloxAfterPlayback = true;

    [ObservableProperty]
    private bool isProxyRunning;

    [ObservableProperty]
    private int proxyPort = 8877;

    [ObservableProperty]
    private string sessionStatusText = "Waiting for refresh";

    [ObservableProperty]
    private string robloxMuteStatus = "Live";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplacementModeText))]
    private bool autoApplyCacheReplacements;

    [ObservableProperty]
    private string deviceId = string.Empty;

    [ObservableProperty]
    private string songIndexBaseUrl = string.Empty;

    [ObservableProperty]
    private bool useDarkMode;

    [ObservableProperty]
    private string uploadSongName = string.Empty;

    [ObservableProperty]
    private string uploadArtist = string.Empty;

    [ObservableProperty]
    private string uploadUploaderName = string.Empty;

    [ObservableProperty]
    private string uploadAudioUrl = string.Empty;

    [ObservableProperty]
    private string uploadStatusText = "Link submissions are ready once a live API exists.";

    [ObservableProperty]
    private Uri? previewAudioSource;

    [ObservableProperty]
    private string previewHeading = "No preview loaded";

    [ObservableProperty]
    private string previewStatusText = "Paste a direct audio URL or choose one of your uploads to hear a preview.";

    [ObservableProperty]
    private string songSearchQuery = string.Empty;

    [ObservableProperty]
    private string songSearchStatusText = "Search songs from the public index API.";

    public MainViewModel(
        SettingsStore settingsStore,
        AudioDeviceService deviceService,
        RobloxAudioSessionService sessionService,
        ReplacementPlaybackService playbackService,
        ProxyFallbackService proxyService,
        RobloxPlayerLogService playerLogService,
        RobloxSoundCacheService soundCacheService,
        RobloxAssetDownloadService assetDownloadService,
        ReplacementSourceService replacementSourceService,
        SongIndexService songIndexService)
    {
        this.settingsStore = settingsStore;
        this.deviceService = deviceService;
        this.sessionService = sessionService;
        this.playbackService = playbackService;
        this.proxyService = proxyService;
        this.playerLogService = playerLogService;
        this.soundCacheService = soundCacheService;
        this.assetDownloadService = assetDownloadService;
        this.replacementSourceService = replacementSourceService;
        this.songIndexService = songIndexService;

        proxyService.AssetDetected += OnAssetDetected;
        proxyService.RequestObserved += OnProxyRequestObserved;
    }

    public int RobloxSessionCount => RobloxSessions.Count;

    public int RuleCount => Rules.Count;

    public string SelectedOutputDeviceName => SelectedOutputDevice?.Name ?? "System default";

    public string OutputVolumeLabel => $"{OutputVolumePercent}%";

    public string ReplacementModeText => AutoApplyCacheReplacements
        ? "Auto cache re-check is on"
        : "Manual apply only";

    public string SongIndexSiteUrl => songIndexService.GetSiteBaseUrl(SongIndexBaseUrl);

    public bool HasPreviewAudio => PreviewAudioSource is not null;

    public async Task InitializeAsync()
    {
        sessionService.SetMute(false);
        robloxMuted = false;
        RobloxMuteStatus = "Live";

        settings = await settingsStore.LoadAsync();

        OutputVolumePercent = settings.OutputVolumePercent;
        selectedOutputDeviceId = settings.PreferredOutputDeviceId;
        AutoReplaceOnRobloxAudioActivity = false;
        EnableProxyFallback = false;
        EnableExperimentalProxyReplacement = false;
        AutoReplaceOnDetection = true;
        AutoApplyCacheReplacements = settings.AutoApplyCacheReplacements;
        AutoMuteRobloxDuringPlayback = settings.AutoMuteRobloxDuringPlayback;
        AutoRestoreRobloxAfterPlayback = settings.AutoRestoreRobloxAfterPlayback;
        ProxyPort = settings.ProxyPort;
        DeviceId = string.IsNullOrWhiteSpace(settings.DeviceId)
            ? CreateDeviceId()
            : settings.DeviceId;
        settings.DeviceId = DeviceId;
        SongIndexBaseUrl = string.IsNullOrWhiteSpace(settings.SongIndexBaseUrl)
            ? songIndexService.GetDefaultSiteBaseUrl()
            : settings.SongIndexBaseUrl;
        settings.SongIndexBaseUrl = SongIndexBaseUrl;
        UseDarkMode = settings.UseDarkMode;

        Rules = new ObservableCollection<ReplacementRule>(settings.Rules.Select(rule => new ReplacementRule
        {
            Name = rule.Name,
            AssetIdPattern = rule.AssetIdPattern,
            FilePath = rule.FilePath,
            IsEnabled = rule.IsEnabled,
            GainPercent = rule.GainPercent,
            SourceAssetHash = rule.SourceAssetHash,
            SourceAssetLength = rule.SourceAssetLength,
            ReplacementFileHash = rule.ReplacementFileHash,
            ReplacementFileLength = rule.ReplacementFileLength,
            PreparedAt = rule.PreparedAt,
            PreparationVersion = rule.PreparationVersion,
            ReplacementSourceWasConverted = rule.ReplacementSourceWasConverted,
        }));
        AttachRuleEvents(Rules);

        UploadedSongs = new ObservableCollection<UploadedSongRecord>(settings.UploadedSongs
            .Where(IsValidUploadedSong)
            .Select(song => new UploadedSongRecord
            {
                Code = song.Code,
                SongName = song.SongName,
                Artist = song.Artist,
                UploaderName = song.UploaderName,
                UploadedByDeviceId = song.UploadedByDeviceId,
                AudioUrl = song.AudioUrl,
                UploadedAt = song.UploadedAt,
            }));
        var removedInvalidUploadedSongs = settings.UploadedSongs.Count != UploadedSongs.Count;

        SelectedRule = Rules.FirstOrDefault();
        SelectedUploadedSong = UploadedSongs.FirstOrDefault();

        await RefreshAsync();

        if (!string.IsNullOrWhiteSpace(selectedOutputDeviceId))
        {
            SelectedOutputDevice = RenderDevices.FirstOrDefault(device =>
                string.Equals(device.Id, selectedOutputDeviceId, StringComparison.OrdinalIgnoreCase));
        }

        SelectedOutputDevice ??= RenderDevices.FirstOrDefault(device => device.IsDefault) ?? RenderDevices.FirstOrDefault();
        selectedOutputDeviceId = SelectedOutputDevice?.Id;
        settings.PreferredOutputDeviceId = selectedOutputDeviceId;

        proxyService.UpdateRules(Rules);
        await UpdateCachedSoundFilesAsync();

        if (AutoApplyCacheReplacements)
        {
            await StartOrStopProxyAsync(forceStart: true);
        }

        StartRobloxAudioMonitor();
        initializationComplete = true;
        AddActivity("Startup", "App initialized in cache replacement mode.");

        if (removedInvalidUploadedSongs)
        {
            await SaveConfigurationAsync(logActivity: false);
            AddActivity("Upload", "Removed broken saved upload entries from local settings.");
        }
    }

    public async Task ShutdownAsync()
    {
        if (monitorCancellationTokenSource is not null)
        {
            monitorCancellationTokenSource.Cancel();
        }

        if (monitorTask is not null)
        {
            try
            {
                await monitorTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        sessionService.SetMute(false);
        robloxMuted = false;
        RobloxMuteStatus = "Live";

        AutoApplyCacheReplacements = false;
        await proxyService.StopAsync();
        IsProxyRunning = false;
        EnableProxyFallback = false;
        await SaveConfigurationAsync(logActivity: false);
        playbackService.Dispose();
        proxyService.Dispose();
    }

    [RelayCommand]
    private Task Refresh() => ResetAndRefreshAsync();

    [RelayCommand]
    private async Task AddRuleAsync()
    {
        var rule = new ReplacementRule
        {
            Name = $"Rule {Rules.Count + 1}",
            GainPercent = OutputVolumePercent,
        };

        Rules.Add(rule);
        AttachRuleEvents(rule);
        SelectedRule = rule;
        proxyService.UpdateRules(Rules);
        OnPropertyChanged(nameof(RuleCount));
        AddActivity("Rules", $"Added {rule.Name}.");
        await SaveConfigurationAsync();
    }

    [RelayCommand]
    private async Task RemoveSelectedRuleAsync()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var removedRuleName = SelectedRule.Name;
        DetachRuleEvents(SelectedRule);
        observedCacheRules.Remove(SelectedRule);
        Rules.Remove(SelectedRule);
        SelectedRule = Rules.FirstOrDefault();
        proxyService.UpdateRules(Rules);
        OnPropertyChanged(nameof(RuleCount));
        AddActivity("Rules", $"Removed {removedRuleName}.");
        await SaveConfigurationAsync();
    }

    [RelayCommand]
    private async Task BrowseSelectedRuleAsync()
    {
        await EnsureSelectedRuleAsync();

        if (SelectedRule is null)
        {
            AddActivity("Rules", "Browse was requested but no rule could be created.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Supported Audio|*.mp3;*.wav;*.ogg;*.m4a;*.aac;*.wma;*.flac;*.m4b;*.mp4|All Files|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Choose replacement audio",
        };

        var result = Application.Current.Dispatcher.Invoke(() =>
            dialog.ShowDialog(Application.Current.MainWindow));

        if (result == true)
        {
            SelectedRule.FilePath = dialog.FileName;
            SelectedRule.ReplacementFileLength = new FileInfo(dialog.FileName).Length;
            SelectedRule.ReplacementFileHash = string.Empty;
            SelectedRule = SelectedRule;
            OnPropertyChanged(nameof(SelectedRule));
            AddActivity("Rules", $"Attached source to {SelectedRule.Name}: {Path.GetFileName(dialog.FileName)}");
            await SaveConfigurationAsync();
        }
    }

    [RelayCommand]
    private void CopyDeviceId()
    {
        if (string.IsNullOrWhiteSpace(DeviceId))
        {
            UploadStatusText = "Device ID is not available yet.";
            return;
        }

        Clipboard.SetText(DeviceId);
        UploadStatusText = "Copied device ID to the clipboard.";
        AddActivity("Upload", "Copied this device ID to the clipboard.");
    }

    [RelayCommand(CanExecute = nameof(CanPreviewTypedAudio))]
    private async Task PreviewTypedAudioAsync()
    {
        await BeginPreviewAsync(
            UploadAudioUrl,
            string.IsNullOrWhiteSpace(UploadSongName)
                ? "Typed audio URL"
                : $"{UploadSongName} - {UploadArtist}".Trim(' ', '-'));
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedUpload))]
    private async Task PreviewSelectedUploadAsync()
    {
        if (SelectedUploadedSong is null)
        {
            return;
        }

        if (!IsValidUploadedSong(SelectedUploadedSong))
        {
            PreviewStatusText = "That saved upload is missing song metadata or a valid direct audio URL.";
            return;
        }

        await BeginPreviewAsync(SelectedUploadedSong.AudioUrl, SelectedUploadedSong.SummaryDisplay);
    }

    [RelayCommand]
    private void StopPreview()
    {
        PreviewAudioSource = null;
        PreviewHeading = "No preview loaded";
        PreviewStatusText = "Preview stopped.";
        OnPropertyChanged(nameof(HasPreviewAudio));
    }

    [RelayCommand]
    private async Task UploadAudioAsync()
    {
        if (string.IsNullOrWhiteSpace(UploadAudioUrl))
        {
            UploadStatusText = "Paste a direct audio URL before submitting.";
            return;
        }

        if (string.IsNullOrWhiteSpace(UploadSongName) || string.IsNullOrWhiteSpace(UploadArtist))
        {
            UploadStatusText = "Song name and artist are required.";
            return;
        }

        try
        {
            UploadStatusText = "Submitting link to the song index API...";
            var uploadedSong = await songIndexService.SubmitSongLinkAsync(
                UploadAudioUrl,
                UploadSongName,
                UploadArtist,
                UploadUploaderName,
                DeviceId,
                SongIndexBaseUrl);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existingSong = UploadedSongs.FirstOrDefault(song =>
                    string.Equals(song.Code, uploadedSong.Code, StringComparison.OrdinalIgnoreCase));

                if (existingSong is not null)
                {
                    UploadedSongs.Remove(existingSong);
                }

                UploadedSongs.Insert(0, uploadedSong);
                SelectedUploadedSong = uploadedSong;
            });

            UploadStatusText = $"Saved {uploadedSong.SongName} as {uploadedSong.Code}.";
            AddActivity("Upload", $"Saved {uploadedSong.SongName} ({uploadedSong.Code}) to the song index.");

            UploadSongName = string.Empty;
            UploadArtist = string.Empty;
            UploadUploaderName = string.Empty;
            UploadAudioUrl = string.Empty;

            await SaveConfigurationAsync(logActivity: false);
        }
        catch (Exception exception)
        {
            UploadStatusText = exception.Message;
            AddActivity("Upload", $"Upload failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedUploadAsync()
    {
        if (SelectedUploadedSong is null)
        {
            UploadStatusText = "Choose one of your submitted songs first.";
            return;
        }

        if (!string.Equals(SelectedUploadedSong.UploadedByDeviceId, DeviceId, StringComparison.Ordinal))
        {
            UploadStatusText = "This device did not upload that song.";
            return;
        }

        try
        {
            UploadStatusText = $"Deleting {SelectedUploadedSong.Code}...";
            await songIndexService.DeleteSongAsync(SelectedUploadedSong.Code, DeviceId, SongIndexBaseUrl);

            var deletedCode = SelectedUploadedSong.Code;
            var deletedName = SelectedUploadedSong.SongName;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UploadedSongs.Remove(SelectedUploadedSong);
                SelectedUploadedSong = UploadedSongs.FirstOrDefault();
            });

            UploadStatusText = $"Deleted {deletedCode}.";
            AddActivity("Upload", $"Deleted uploaded song {deletedName} ({deletedCode}).");
            await SaveConfigurationAsync(logActivity: false);
        }
        catch (Exception exception)
        {
            UploadStatusText = exception.Message;
            AddActivity("Upload", $"Delete failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task SearchSongsAsync()
    {
        try
        {
            SongSearchStatusText = "Loading songs from the index API...";
            var results = await songIndexService.SearchSongsAsync(SongSearchQuery, SongIndexBaseUrl);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SongSearchResults = new ObservableCollection<SongIndexEntry>(results);
                SelectedSongSearchResult = SongSearchResults.FirstOrDefault();
            });

            SongSearchStatusText = results.Count == 0
                ? "No songs matched that search."
                : $"Loaded {results.Count} song{(results.Count == 1 ? string.Empty : "s")} from the index API.";
            AddActivity("Songs", string.IsNullOrWhiteSpace(SongSearchQuery)
                ? $"Loaded {results.Count} song result(s) from the index API."
                : $"Found {results.Count} song result(s) for \"{SongSearchQuery.Trim()}\".");
        }
        catch (Exception exception)
        {
            SongSearchStatusText = exception.Message;
            AddActivity("Songs", $"Song search failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadAllSongsAsync()
    {
        SongSearchQuery = string.Empty;
        await SearchSongsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanPreviewSelectedSongSearchResult))]
    private async Task PreviewSelectedSongSearchResultAsync()
    {
        if (SelectedSongSearchResult is null)
        {
            return;
        }

        await BeginPreviewAsync(
            SelectedSongSearchResult.AudioUrl,
            $"{SelectedSongSearchResult.SongName} - {SelectedSongSearchResult.Artist}".Trim(' ', '-'));
    }

    [RelayCommand]
    private void CopySelectedSongCode()
    {
        if (SelectedSongSearchResult is null)
        {
            return;
        }

        Clipboard.SetText(SelectedSongSearchResult.Code);
        SongSearchStatusText = $"Copied {SelectedSongSearchResult.Code}.";
        AddActivity("Songs", $"Copied song code {SelectedSongSearchResult.Code}.");
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedSongCode))]
    private async Task UseSelectedSongCodeAsync()
    {
        if (SelectedSongSearchResult is null)
        {
            return;
        }

        await EnsureSelectedRuleAsync();
        if (SelectedRule is null)
        {
            SongSearchStatusText = "Could not create or select a rule.";
            return;
        }

        SelectedRule.FilePath = SelectedSongSearchResult.Code;
        SelectedRule.ReplacementFileHash = string.Empty;
        SelectedRule.ReplacementFileLength = 0;
        SelectedRule = SelectedRule;
        OnPropertyChanged(nameof(SelectedRule));

        SongSearchStatusText = $"Set {SelectedRule.Name} to use song code {SelectedSongSearchResult.Code}.";
        AddActivity("Songs", $"Applied song code {SelectedSongSearchResult.Code} to {SelectedRule.Name}.");
        await SaveConfigurationAsync(logActivity: false);
    }

    [RelayCommand]
    private async Task PlaySelectedRuleAsync()
    {
        if (SelectedRule is null)
        {
            return;
        }

        await PlayRuleAsync(SelectedRule, "manual preview");
    }

    [RelayCommand]
    private async Task ToggleRobloxMuteAsync()
    {
        robloxMuted = !robloxMuted;
        sessionService.SetMute(robloxMuted);
        RobloxMuteStatus = robloxMuted ? "Muted" : "Live";
        AddActivity("Audio", robloxMuted ? "Muted Roblox sessions." : "Unmuted Roblox sessions.");
        await RefreshAsync();
    }

    [RelayCommand]
    private Task SaveConfiguration() => SaveConfigurationAsync();

    [RelayCommand]
    private async Task PrepareRuleAssetsAsync()
    {
        var candidateRules = Rules
            .Where(rule => rule.IsEnabled)
            .ToList();

        if (candidateRules.Count == 0)
        {
            AddActivity("Prepare", "No enabled rules are available to prepare.");
            return;
        }

        var preparedCount = 0;
        foreach (var rule in candidateRules)
        {
            var wasPrepared = IsRulePrepared(rule);
            if (await EnsureRulePreparedAsync(rule, logSkipMessage: true) && !wasPrepared)
            {
                preparedCount++;
            }
        }

        await SaveConfigurationAsync();
        await UpdateCachedSoundFilesAsync();
        AddActivity("Prepare", preparedCount == 0
            ? "No rules were prepared."
            : $"Prepared {preparedCount} rule(s) for cache replacement.");
    }

    [RelayCommand]
    private async Task SaveAndApplySelectedRuleAsync()
    {
        await EnsureSelectedRuleAsync();

        if (SelectedRule is null)
        {
            return;
        }

        var rule = SelectedRule;
        if (!rule.IsEnabled)
        {
            var restoredCount = await RestoreRuleToOriginalAsync(rule, "saved as disabled");
            await SaveConfigurationAsync();
            await UpdateCachedSoundFilesAsync();

            AddActivity(
                "Rules",
                restoredCount > 0
                    ? $"Saved {rule.Name} as original audio and restored {restoredCount} cached file(s)."
                    : $"Saved {rule.Name} as original audio.");
            return;
        }

        var prepared = await EnsureRulePreparedAsync(rule, logSkipMessage: false);
        await SaveConfigurationAsync();

        if (!prepared)
        {
            return;
        }

        await ApplyPreparedCacheReplacementsAsync(
            "save and apply",
            TryGetExactAssetId(rule.AssetIdPattern));
        await SaveConfigurationAsync();
        AddActivity("Rules", $"Saved and applied {rule.Name}.");
    }

    [RelayCommand]
    private async Task ApplyAllRulesAsync()
    {
        var enabledRules = Rules
            .Where(rule => rule.IsEnabled)
            .ToList();

        if (enabledRules.Count == 0)
        {
            AddActivity("Rules", "There are no enabled rules to apply.");
            return;
        }

        var preparedCount = 0;
        foreach (var rule in enabledRules)
        {
            var wasPrepared = IsRulePrepared(rule);
            var prepared = await EnsureRulePreparedAsync(rule, logSkipMessage: false);
            if (prepared && !wasPrepared)
            {
                preparedCount++;
            }
        }

        await SaveConfigurationAsync();
        await ApplyPreparedCacheReplacementsAsync("apply all");
        await SaveConfigurationAsync(logActivity: false);

        AddActivity(
            "Rules",
            preparedCount > 0
                ? $"Applied all enabled rules and prepared {preparedCount} new rule(s)."
                : "Applied all enabled rules.");
    }

    [RelayCommand]
    private async Task ApplyCacheReplacementsAsync()
    {
        await ApplyPreparedCacheReplacementsAsync("manual apply");
    }

    private async Task ResetAndRefreshAsync()
    {
        var restoredCount = await RestoreAllCachedSoundFilesToOriginalAsync();

        foreach (var rule in Rules)
        {
            InvalidatePreparedState(rule);
        }

        var preparedCount = 0;
        var enabledRules = Rules
            .Where(rule => rule.IsEnabled)
            .ToList();

        foreach (var rule in enabledRules)
        {
            if (await EnsureRulePreparedAsync(rule, logSkipMessage: false))
            {
                preparedCount++;
            }
        }

        await SaveConfigurationAsync(logActivity: false);
        await RefreshAsync();
        await ApplyPreparedCacheReplacementsAsync("refresh rebuild");
        await SaveConfigurationAsync(logActivity: false);

        AddActivity(
            "Refresh",
            $"Refresh restored {restoredCount} cached file(s), re-prepared {preparedCount} enabled rule(s), and rechecked the Roblox sound cache.");
    }

    private async Task RefreshAsync()
    {
        var sessions = sessionService.GetRobloxSessions();
        var devices = deviceService.GetRenderDevices();
        await ApplySnapshotAsync(devices, sessions);
        await UpdateCachedSoundFilesAsync();
    }

    private async Task EnsureSelectedRuleAsync()
    {
        if (SelectedRule is not null)
        {
            return;
        }

        if (Rules.Count == 0)
        {
            var rule = new ReplacementRule
            {
                Name = "Rule 1",
                GainPercent = OutputVolumePercent,
            };

            Rules.Add(rule);
            AttachRuleEvents(rule);
            OnPropertyChanged(nameof(RuleCount));
        }

        SelectedRule = Rules.FirstOrDefault();
        proxyService.UpdateRules(Rules);
        await SaveConfigurationAsync();
    }

    private async Task SaveConfigurationAsync(bool logActivity = true)
    {
        settings.DeviceId = DeviceId;
        settings.SongIndexBaseUrl = SongIndexBaseUrl;
        settings.UseDarkMode = UseDarkMode;
        settings.PreferredOutputDeviceId = selectedOutputDeviceId ?? SelectedOutputDevice?.Id;
        settings.OutputVolumePercent = OutputVolumePercent;
        settings.AutoReplaceOnRobloxAudioActivity = false;
        settings.EnableProxyFallback = false;
        settings.EnableExperimentalProxyReplacement = false;
        settings.AutoReplaceOnDetection = true;
        settings.AutoApplyCacheReplacements = AutoApplyCacheReplacements;
        settings.AutoMuteRobloxDuringPlayback = AutoMuteRobloxDuringPlayback;
        settings.AutoRestoreRobloxAfterPlayback = AutoRestoreRobloxAfterPlayback;
        settings.ProxyPort = ProxyPort;
        settings.Rules = Rules.Select(rule => new ReplacementRule
        {
            Name = rule.Name,
            AssetIdPattern = rule.AssetIdPattern,
            FilePath = rule.FilePath,
            IsEnabled = rule.IsEnabled,
            GainPercent = rule.GainPercent,
            SourceAssetHash = rule.SourceAssetHash,
            SourceAssetLength = rule.SourceAssetLength,
            ReplacementFileHash = rule.ReplacementFileHash,
            ReplacementFileLength = rule.ReplacementFileLength,
            PreparedAt = rule.PreparedAt,
            PreparationVersion = rule.PreparationVersion,
            ReplacementSourceWasConverted = rule.ReplacementSourceWasConverted,
        }).ToList();
        settings.UploadedSongs = UploadedSongs
            .Where(IsValidUploadedSong)
            .Select(song => new UploadedSongRecord
            {
                Code = song.Code,
                SongName = song.SongName,
                Artist = song.Artist,
                UploaderName = song.UploaderName,
                UploadedByDeviceId = song.UploadedByDeviceId,
                AudioUrl = song.AudioUrl,
                UploadedAt = song.UploadedAt,
            }).ToList();

        await settingsStore.SaveAsync(settings);
        proxyService.UpdateRules(Rules);

        if (logActivity)
        {
            AddActivity("Config", "Saved configuration to AppData.");
        }
    }

    private void StartRobloxAudioMonitor()
    {
        monitorCancellationTokenSource = new CancellationTokenSource();
        monitorTask = Task.Run(() => MonitorRobloxAudioAsync(monitorCancellationTokenSource.Token));
    }

    private void AttachRuleEvents(IEnumerable<ReplacementRule> rules)
    {
        foreach (var rule in rules)
        {
            AttachRuleEvents(rule);
        }
    }

    private void AttachRuleEvents(ReplacementRule rule)
    {
        rule.PropertyChanged -= OnRulePropertyChanged;
        rule.PropertyChanged += OnRulePropertyChanged;
    }

    private void DetachRuleEvents(ReplacementRule rule)
    {
        rule.PropertyChanged -= OnRulePropertyChanged;
    }

    private async Task MonitorRobloxAudioAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(750));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var sessions = sessionService.GetRobloxSessions();
            var devices = deviceService.GetRenderDevices();
            await ApplySnapshotAsync(devices, sessions);
            await ProcessRobloxAssetSignalsAsync();

            lock (monitorStateLock)
            {
                lastRobloxAudioActivityState = sessions.Any(session => session.HasAudibleActivity);
            }
        }
    }

    private async Task ApplySnapshotAsync(
        IReadOnlyList<AudioDeviceInfo> devices,
        IReadOnlyList<RobloxAudioSessionInfo> sessions)
    {
        var currentIdentifiers = sessions
            .Select(session => session.SessionIdentifier)
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int openedCount;
        int closedCount;

        lock (monitorStateLock)
        {
            openedCount = currentIdentifiers.Except(lastSessionIdentifiers, StringComparer.OrdinalIgnoreCase).Count();
            closedCount = lastSessionIdentifiers.Except(currentIdentifiers, StringComparer.OrdinalIgnoreCase).Count();
            lastSessionIdentifiers = currentIdentifiers;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            RenderDevices = new ObservableCollection<AudioDeviceInfo>(devices);

            var preferredDeviceId = selectedOutputDeviceId ?? settings.PreferredOutputDeviceId;
            var resolvedSelection = !string.IsNullOrWhiteSpace(preferredDeviceId)
                ? RenderDevices.FirstOrDefault(device =>
                    string.Equals(device.Id, preferredDeviceId, StringComparison.OrdinalIgnoreCase))
                : null;

            resolvedSelection ??= RenderDevices.FirstOrDefault(device => device.IsDefault) ?? RenderDevices.FirstOrDefault();

            SelectedOutputDevice = resolvedSelection;
            selectedOutputDeviceId = resolvedSelection?.Id;
            settings.PreferredOutputDeviceId = selectedOutputDeviceId;

            RobloxSessions = new ObservableCollection<RobloxAudioSessionInfo>(sessions);

            SessionStatusText = sessions.Count switch
            {
                0 => "No active Roblox session detected",
                _ when sessions.Any(session => session.HasAudibleActivity) => $"{sessions.Count} Roblox session(s) active, audio playing",
                _ => $"{sessions.Count} Roblox session(s) active",
            };

            OnPropertyChanged(nameof(RobloxSessionCount));
            OnPropertyChanged(nameof(RuleCount));
        });

        if (openedCount > 0)
        {
            AddActivity("Monitor", openedCount == 1
                ? "A Roblox session opened."
                : $"{openedCount} Roblox sessions opened.");
        }

        if (closedCount > 0)
        {
            AddActivity("Monitor", closedCount == 1
                ? "A Roblox session closed."
                : $"{closedCount} Roblox sessions closed.");
        }
    }

    private async Task ProcessRobloxAssetSignalsAsync()
    {
        var resolvedAssets = playerLogService.PollResolvedAssets();
        foreach (var resolution in resolvedAssets)
        {
            await HandleResolvedAssetAsync(resolution);
        }

        var cacheChanges = soundCacheService.PollCacheChanges();
        if (cacheChanges.Count > 0)
        {
            await UpdateCachedSoundFilesAsync();
        }

        foreach (var cacheEntry in cacheChanges)
        {
            await HandleCacheEntryAsync(cacheEntry);
        }
    }

    private async Task HandleResolvedAssetAsync(RobloxAssetResolutionInfo resolution)
    {
        AddActivity("Log", $"Roblox resolved asset {resolution.AssetId}.");

        if (AutoApplyCacheReplacements)
        {
            await ApplyPreparedCacheReplacementsAsync($"log asset {resolution.AssetId}", resolution.AssetId);
        }
    }

    private async Task HandleCacheEntryAsync(RobloxSoundCacheEntry cacheEntry)
    {
        AddActivity("Cache", $"Roblox sound cache updated: {cacheEntry.FileName}");

        if (AutoApplyCacheReplacements)
        {
            await TryApplyPreparedReplacementToCacheEntryAsync(cacheEntry, "cache refresh");
        }
    }

    private async Task StartOrStopProxyAsync(bool forceStart)
    {
        if (forceStart)
        {
            try
            {
                proxyService.UpdateRules(Rules);
                var status = await proxyService.StartAsync(ProxyPort, EnableExperimentalProxyReplacement);
                IsProxyRunning = status.IsRunning;
                EnableProxyFallback = status.IsRunning;
                AddActivity("Replace", "Background Roblox asset watch is running.");
            }
            catch (Exception exception)
            {
                var detailedMessage = exception.InnerException is null
                    ? exception.Message
                    : $"{exception.Message} Inner error: {exception.InnerException.Message}";

                IsProxyRunning = false;
                EnableProxyFallback = false;
                sessionService.SetMute(false);
                robloxMuted = false;
                RobloxMuteStatus = "Live";
                AddActivity("Replace", $"Background Roblox asset watch could not start: {detailedMessage}");
            }
        }
        else
        {
            var status = await proxyService.StopAsync();
            IsProxyRunning = status.IsRunning;
            EnableProxyFallback = false;
            AddActivity("Replace", "Background Roblox asset watch stopped.");
        }
    }

    private async void OnAssetDetected(object? sender, ProxyAssetDetectedEventArgs eventArgs)
    {
        var matchedRule = Rules.FirstOrDefault(rule =>
            rule.IsEnabled
            && HasReplacementSource(rule)
            && RuleMatcher.Matches(rule.AssetIdPattern, eventArgs.AssetId));

        if (matchedRule is null)
        {
            AddActivity("Detection", $"Proxy saw asset {eventArgs.AssetId}, but no enabled local rule matched it.");
            return;
        }

        AddActivity("Detection", $"Matched asset {eventArgs.AssetId} to {matchedRule.Name}; re-checking cache.");

        if (AutoApplyCacheReplacements)
        {
            await ApplyPreparedCacheReplacementsAsync($"proxy match {eventArgs.AssetId}", eventArgs.AssetId);
        }
    }

    private async Task PlayRuleAsync(ReplacementRule rule, string reason)
    {
        var replacementSource = await ResolveReplacementSourceAsync(rule, forceRefreshRemote: false);
        if (replacementSource is null)
        {
            AddActivity("Playback", $"Skipped {rule.Name} because the replacement source could not be resolved.");
            return;
        }

        await playbackLock.WaitAsync();
        var mutedForThisPlayback = false;
        try
        {
            playbackService.ValidatePlayback(replacementSource.LocalPath, SelectedOutputDevice?.Id);

            if (AutoMuteRobloxDuringPlayback)
            {
                sessionService.SetMute(true);
                robloxMuted = true;
                RobloxMuteStatus = "Muted";
                mutedForThisPlayback = true;
            }

            AddActivity("Playback", $"Playing {rule.Name} for {reason}.");
            await playbackService.PlayAsync(
                replacementSource.LocalPath,
                SelectedOutputDevice?.Id,
                Math.Clamp(rule.GainPercent / 100f, 0f, 1f));
        }
        catch (Exception exception)
        {
            if (mutedForThisPlayback)
            {
                sessionService.SetMute(false);
                robloxMuted = false;
                RobloxMuteStatus = "Live";
            }

            AddActivity("Playback", $"Playback failed for {rule.Name}: {exception.Message}");
        }
        finally
        {
            if (mutedForThisPlayback && AutoRestoreRobloxAfterPlayback)
            {
                sessionService.SetMute(false);
                robloxMuted = false;
                RobloxMuteStatus = "Live";
            }

            playbackLock.Release();
            await RefreshAsync();
        }
    }

    private void AddActivity(string category, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Activity.Insert(0, new ActivityLogEntry
            {
                Category = category,
                Message = message,
                Timestamp = DateTimeOffset.Now,
            });

            while (Activity.Count > 40)
            {
                Activity.RemoveAt(Activity.Count - 1);
            }
        });
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceInfo? value)
    {
        selectedOutputDeviceId = value?.Id;
        settings.PreferredOutputDeviceId = selectedOutputDeviceId;
        OnPropertyChanged(nameof(SelectedOutputDeviceName));
    }

    private void OnProxyRequestObserved(object? sender, ProxyRequestObservedEventArgs eventArgs)
    {
        if (eventArgs.IsAssetDeliveryRequest)
        {
            if (AutoApplyCacheReplacements && !string.IsNullOrWhiteSpace(eventArgs.AssetId))
            {
                _ = Task.Run(() => ApplyPreparedCacheReplacementsAsync("assetdelivery re-check", eventArgs.AssetId));
            }

            AddActivity("Replace", BuildProxyActivityMessage(eventArgs));
        }
    }

    private static string BuildProxyActivityMessage(ProxyRequestObservedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.AssetId))
        {
            return $"Saw an assetdelivery request at {eventArgs.Host}, but no asset ID was parsed.";
        }

        if (!string.IsNullOrWhiteSpace(eventArgs.MatchedRuleName))
        {
            return $"Asset {eventArgs.AssetId} matched {eventArgs.MatchedRuleName}; checking Roblox's local sound cache.";
        }

        return $"Asset {eventArgs.AssetId} was requested, but no prepared rule matched it.";
    }

    private async Task UpdateCachedSoundFilesAsync()
    {
        var snapshot = soundCacheService.GetSoundCacheSnapshot();
        var cacheRulesChanged = HandleRuleCachePresence(snapshot);
        var annotated = snapshot
            .Select(AnnotateCacheEntry)
            .ToList();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CachedSoundFiles = new ObservableCollection<RobloxSoundCacheEntry>(annotated);
        });

        if (cacheRulesChanged)
        {
            await SaveConfigurationAsync(logActivity: false);
        }
    }

    private RobloxSoundCacheEntry AnnotateCacheEntry(RobloxSoundCacheEntry entry)
    {
        var preparedMatch = Rules.FirstOrDefault(rule =>
            rule.IsEnabled
            && !string.IsNullOrWhiteSpace(rule.SourceAssetHash)
            && string.Equals(rule.SourceAssetHash, entry.Sha256, StringComparison.OrdinalIgnoreCase));

        if (preparedMatch is not null)
        {
            return new RobloxSoundCacheEntry
            {
                FileName = entry.FileName,
                FullPath = entry.FullPath,
                Length = entry.Length,
                LastWriteTime = entry.LastWriteTime,
                Sha256 = entry.Sha256,
                MatchedRuleName = preparedMatch.Name,
                MatchedAssetId = TryGetExactAssetId(preparedMatch.AssetIdPattern) ?? preparedMatch.AssetIdPattern,
                MatchedSourceAssetLength = preparedMatch.SourceAssetLength,
                MatchedReplacementFileLength = preparedMatch.ReplacementFileLength,
                StatusText = "Match ready",
            };
        }

        var replacedMatch = Rules.FirstOrDefault(rule =>
            rule.IsEnabled
            && !string.IsNullOrWhiteSpace(rule.ReplacementFileHash)
            && string.Equals(rule.ReplacementFileHash, entry.Sha256, StringComparison.OrdinalIgnoreCase));

        if (replacedMatch is not null)
        {
            return new RobloxSoundCacheEntry
            {
                FileName = entry.FileName,
                FullPath = entry.FullPath,
                Length = entry.Length,
                LastWriteTime = entry.LastWriteTime,
                Sha256 = entry.Sha256,
                MatchedRuleName = replacedMatch.Name,
                MatchedAssetId = TryGetExactAssetId(replacedMatch.AssetIdPattern) ?? replacedMatch.AssetIdPattern,
                MatchedSourceAssetLength = replacedMatch.SourceAssetLength,
                MatchedReplacementFileLength = replacedMatch.ReplacementFileLength,
                StatusText = "Replaced",
            };
        }

        return new RobloxSoundCacheEntry
        {
            FileName = entry.FileName,
            FullPath = entry.FullPath,
            Length = entry.Length,
            LastWriteTime = entry.LastWriteTime,
            Sha256 = entry.Sha256,
            StatusText = "Original",
        };
    }

    private async Task ApplyPreparedCacheReplacementsAsync(string reason, string? specificAssetId = null)
    {
        var snapshot = soundCacheService.GetSoundCacheSnapshot();
        var preparedRules = Rules
            .Where(rule =>
                rule.IsEnabled
                && !string.IsNullOrWhiteSpace(rule.SourceAssetHash)
                && !string.IsNullOrWhiteSpace(rule.ReplacementFileHash)
                && HasReplacementSource(rule))
            .Where(rule => string.IsNullOrWhiteSpace(specificAssetId)
                || string.Equals(TryGetExactAssetId(rule.AssetIdPattern), specificAssetId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (preparedRules.Count == 0)
        {
            return;
        }

        var replacementsApplied = 0;
        foreach (var cacheEntry in snapshot)
        {
            var matchedRule = preparedRules.FirstOrDefault(rule =>
                string.Equals(rule.SourceAssetHash, cacheEntry.Sha256, StringComparison.OrdinalIgnoreCase));

            if (matchedRule is null)
            {
                continue;
            }

            var applied = await TryApplyPreparedReplacementToCacheEntryAsync(cacheEntry, reason);
            if (applied)
            {
                replacementsApplied++;
            }
        }

        await UpdateCachedSoundFilesAsync();

        if (replacementsApplied == 0 && string.Equals(reason, "manual apply", StringComparison.OrdinalIgnoreCase))
        {
            AddActivity("Cache", "Manual apply found no prepared cached sounds to replace.");
            LogManualApplySizeDetails(snapshot, preparedRules);
        }
    }

    private async Task<int> RestoreRuleToOriginalAsync(ReplacementRule rule, string reason)
    {
        if (string.IsNullOrWhiteSpace(rule.ReplacementFileHash))
        {
            return 0;
        }

        var snapshot = soundCacheService.GetSoundCacheSnapshot();
        var restoredCount = 0;

        foreach (var cacheEntry in snapshot.Where(entry =>
                     string.Equals(entry.Sha256, rule.ReplacementFileHash, StringComparison.OrdinalIgnoreCase)))
        {
            if (!soundCacheService.RestoreSoundFile(cacheEntry.FullPath))
            {
                AddActivity("Cache", $"Skipped restoring {cacheEntry.FileName} because Roblox is still using it.");
                continue;
            }

            restoredCount++;
            AddActivity("Cache", $"Restored {cacheEntry.FileName} back to Roblox original for {rule.Name} ({reason}).");
        }

        return restoredCount;
    }

    private async Task<int> RestoreAllCachedSoundFilesToOriginalAsync()
    {
        var snapshot = soundCacheService.GetSoundCacheSnapshot();
        var restoredCount = 0;

        foreach (var cacheEntry in snapshot)
        {
            if (!soundCacheService.RestoreSoundFile(cacheEntry.FullPath))
            {
                continue;
            }

            restoredCount++;
            AddActivity("Cache", $"Restored {cacheEntry.FileName} back to the Roblox original during refresh.");
        }

        await UpdateCachedSoundFilesAsync();
        return restoredCount;
    }

    private async Task<bool> TryApplyPreparedReplacementToCacheEntryAsync(RobloxSoundCacheEntry cacheEntry, string reason)
    {
        var matchedRule = Rules.FirstOrDefault(rule =>
            rule.IsEnabled
            && !string.IsNullOrWhiteSpace(rule.SourceAssetHash)
            && !string.IsNullOrWhiteSpace(rule.ReplacementFileHash)
            && HasReplacementSource(rule)
            && string.Equals(rule.SourceAssetHash, cacheEntry.Sha256, StringComparison.OrdinalIgnoreCase));

        if (matchedRule is null)
        {
            return false;
        }

        if (string.Equals(cacheEntry.Sha256, matchedRule.ReplacementFileHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!CanOverwriteCacheFile(cacheEntry.FullPath))
        {
            AddActivity("Cache", $"Skipped {cacheEntry.FileName} because Roblox appears to be using it already.");
            return false;
        }

        var replacementSource = await ResolveReplacementSourceAsync(matchedRule, forceRefreshRemote: false);
        if (replacementSource is null)
        {
            AddActivity("Cache", $"Skipped {matchedRule.Name} because its replacement source could not be resolved.");
            return false;
        }

        if (!soundCacheService.ReplaceSoundFile(cacheEntry.FullPath, replacementSource.LocalPath))
        {
            AddActivity("Cache", $"Skipped {cacheEntry.FileName} because it became busy while trying to replace it.");
            return false;
        }

        AddActivity(
            "Cache",
            $"Replaced {cacheEntry.FileName} using {matchedRule.Name} for asset {TryGetExactAssetId(matchedRule.AssetIdPattern) ?? matchedRule.AssetIdPattern} ({reason}).");
        await UpdateCachedSoundFilesAsync();
        return true;
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not ReplacementRule rule)
        {
            return;
        }

        if (string.Equals(eventArgs.PropertyName, nameof(ReplacementRule.AssetIdPattern), StringComparison.Ordinal)
            || string.Equals(eventArgs.PropertyName, nameof(ReplacementRule.FilePath), StringComparison.Ordinal))
        {
            InvalidatePreparedState(rule);
        }
    }

    private async Task<bool> EnsureRulePreparedAsync(ReplacementRule rule, bool logSkipMessage)
    {
        var assetId = TryGetExactAssetId(rule.AssetIdPattern);
        if (string.IsNullOrWhiteSpace(assetId))
        {
            AddActivity("Prepare", $"Skipped {rule.Name} because the asset pattern is not a single exact asset ID.");
            return false;
        }

        if (!HasReplacementSource(rule))
        {
            AddActivity("Prepare", $"Skipped {rule.Name} because the replacement source is missing.");
            return false;
        }

        if (IsRulePrepared(rule))
        {
            if (logSkipMessage)
            {
                AddActivity("Prepare", $"Skipped {rule.Name} because it is already prepared.");
            }

            return true;
        }

        try
        {
            var replacementSource = await ResolveReplacementSourceAsync(rule, forceRefreshRemote: true);
            if (replacementSource is null)
            {
                AddActivity("Prepare", $"Failed to prepare {rule.Name}: could not download or open the replacement source.");
                return false;
            }

            var downloadedAsset = await assetDownloadService.DownloadAssetInfoAsync(assetId);
            rule.SourceAssetHash = downloadedAsset.Sha256;
            rule.SourceAssetLength = downloadedAsset.Length;
            rule.ReplacementFileHash = soundCacheService.ComputeFileHash(replacementSource.LocalPath);
            rule.ReplacementFileLength = replacementSource.Length;
            rule.ReplacementSourceWasConverted = replacementSource.IsConverted;
            rule.PreparedAt = DateTimeOffset.Now;
            rule.PreparationVersion = ReplacementRule.LatestPreparationVersion;

            AddActivity(
                "Prepare",
                $"Prepared {rule.Name} for asset {assetId}. Original download: {FormatKilobytes(downloadedAsset.Length)}. Replacement source: {FormatKilobytes(replacementSource.Length)}.");
            return true;
        }
        catch (Exception exception)
        {
            AddActivity("Prepare", $"Failed to prepare {rule.Name}: {exception.Message}");
            return false;
        }
    }

    private static bool IsRulePrepared(ReplacementRule rule)
    {
        return rule.IsPrepared && HasReplacementSource(rule);
    }

    private static bool CanOverwriteCacheFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void InvalidatePreparedState(ReplacementRule rule)
    {
        rule.SourceAssetHash = string.Empty;
        rule.SourceAssetLength = 0;
        rule.ReplacementFileHash = string.Empty;
        rule.ReplacementFileLength = !string.IsNullOrWhiteSpace(rule.FilePath)
            && !IsRemoteReplacementSource(rule.FilePath)
            && File.Exists(rule.FilePath)
            ? new FileInfo(rule.FilePath).Length
            : 0;
        rule.PreparedAt = null;
        rule.PreparationVersion = 0;
        rule.ReplacementSourceWasConverted = false;
    }

    private async Task<ResolvedReplacementSource?> ResolveReplacementSourceAsync(ReplacementRule rule, bool forceRefreshRemote)
    {
        if (string.IsNullOrWhiteSpace(rule.FilePath))
        {
            return null;
        }

        var resolvedSource = await ResolveSourceReferenceAsync(rule.FilePath);
        if (string.IsNullOrWhiteSpace(resolvedSource))
        {
            return null;
        }

        return await replacementSourceService.ResolveAsync(resolvedSource, forceRefreshRemote);
    }

    private static bool HasReplacementSource(ReplacementRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.FilePath))
        {
            return false;
        }

        return SongIndexService.LooksLikeSongCode(rule.FilePath)
            || IsRemoteReplacementSource(rule.FilePath)
            || File.Exists(rule.FilePath);
    }

    private static bool IsRemoteReplacementSource(string? source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private bool HandleRuleCachePresence(IReadOnlyList<RobloxSoundCacheEntry> snapshot)
    {
        var anyRuleChanged = false;

        foreach (var rule in Rules)
        {
            if (!rule.IsEnabled || !IsRulePrepared(rule))
            {
                observedCacheRules.Remove(rule);
                continue;
            }

            var cacheIsPresent = snapshot.Any(entry =>
                string.Equals(entry.Sha256, rule.SourceAssetHash, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Sha256, rule.ReplacementFileHash, StringComparison.OrdinalIgnoreCase));

            if (cacheIsPresent)
            {
                observedCacheRules.Add(rule);
                continue;
            }

            if (!observedCacheRules.Remove(rule))
            {
                continue;
            }

            rule.IsEnabled = false;
            InvalidatePreparedState(rule);
            anyRuleChanged = true;
            AddActivity("Cache", $"Disabled {rule.Name} because its Roblox cache file was removed.");
        }

        return anyRuleChanged;
    }

    private static string? TryGetExactAssetId(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var trimmedPattern = pattern.Trim();
        if (trimmedPattern.StartsWith("rbxassetid://", StringComparison.OrdinalIgnoreCase))
        {
            trimmedPattern = trimmedPattern["rbxassetid://".Length..];
        }

        return trimmedPattern.All(char.IsDigit) ? trimmedPattern : null;
    }

    private void LogManualApplySizeDetails(
        IReadOnlyList<RobloxSoundCacheEntry> snapshot,
        IReadOnlyList<ReplacementRule> preparedRules)
    {
        if (preparedRules.Count == 0)
        {
            AddActivity("Cache", "There are no prepared rules with both a source hash and a replacement file.");
            return;
        }

        foreach (var rule in preparedRules)
        {
            var assetId = TryGetExactAssetId(rule.AssetIdPattern) ?? rule.AssetIdPattern;
            AddActivity(
                "Cache",
                $"Prepared rule {rule.Name} for asset {assetId}: original {FormatKilobytes(rule.SourceAssetLength)}, replacement source {FormatKilobytes(rule.ReplacementFileLength)}.");
        }

        if (snapshot.Count == 0)
        {
            AddActivity("Cache", "Roblox sound cache is empty right now, so there was nothing to compare.");
            return;
        }

        var cacheSummary = string.Join(
            ", ",
            snapshot
                .Take(8)
                .Select(entry => $"{entry.FileName}={FormatKilobytes(entry.Length)}"));

        AddActivity(
            "Cache",
            $"Scanned {snapshot.Count} cached sound file(s). Current sizes: {cacheSummary}{(snapshot.Count > 8 ? ", ..." : string.Empty)}");
    }

    private static string FormatKilobytes(long bytes) => bytes <= 0 ? "-" : $"{bytes / 1024d:N1} KB";

    private async Task<string?> ResolveSourceReferenceAsync(string source)
    {
        if (!SongIndexService.LooksLikeSongCode(source))
        {
            return source;
        }

        var song = await songIndexService.ResolveSongCodeAsync(source, SongIndexBaseUrl);
        if (song is not null)
        {
            AddActivity("Songs", $"Resolved song code {song.Code} to {song.SongName} by {song.Artist}.");
            return song.AudioUrl;
        }

        AddActivity("Songs", $"Song code {source.Trim().ToUpperInvariant()} was not found in the public index.");
        return null;
    }

    private static string CreateDeviceId()
    {
        return Convert.ToHexString(Guid.NewGuid().ToByteArray()).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    public void HandlePreviewPlaybackStarted()
    {
        PreviewStatusText = $"Playing {PreviewHeading}.";
    }

    public void HandlePreviewPlaybackFinished()
    {
        if (PreviewAudioSource is null)
        {
            return;
        }

        PreviewStatusText = $"Preview finished for {PreviewHeading}.";
    }

    public void HandlePreviewPlaybackFailed(string? detail)
    {
        PreviewAudioSource = null;
        PreviewStatusText = string.IsNullOrWhiteSpace(detail)
            ? "Preview could not be played."
            : $"Preview could not be played: {detail}";
        OnPropertyChanged(nameof(HasPreviewAudio));
    }

    partial void OnSongIndexBaseUrlChanged(string value)
    {
        OnPropertyChanged(nameof(SongIndexSiteUrl));
        SongSearchStatusText = "Song index base URL changed. Search again to refresh results.";
    }

    partial void OnUseDarkModeChanged(bool value)
    {
        (Application.Current as App)?.ApplyTheme(value);
    }

    partial void OnUploadAudioUrlChanged(string value)
    {
        PreviewTypedAudioCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedUploadedSongChanged(UploadedSongRecord? value)
    {
        PreviewSelectedUploadCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSongSearchResultChanged(SongIndexEntry? value)
    {
        PreviewSelectedSongSearchResultCommand.NotifyCanExecuteChanged();
        UseSelectedSongCodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewAudioSourceChanged(Uri? value)
    {
        OnPropertyChanged(nameof(HasPreviewAudio));
    }

    partial void OnAutoApplyCacheReplacementsChanged(bool value)
    {
        OnPropertyChanged(nameof(ReplacementModeText));

        if (!initializationComplete)
        {
            return;
        }

        _ = Task.Run(() => HandleAutoApplyCacheReplacementsChangedAsync(value));
    }

    private async Task HandleAutoApplyCacheReplacementsChangedAsync(bool value)
    {
        try
        {
            if (value)
            {
                await StartOrStopProxyAsync(forceStart: true);
            }
            else if (IsProxyRunning)
            {
                await StartOrStopProxyAsync(forceStart: false);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            AddActivity("Replace", $"Could not update background asset watch: {exception.Message}");
        }
    }

    private bool CanPreviewTypedAudio()
    {
        return !string.IsNullOrWhiteSpace(UploadAudioUrl);
    }

    private bool CanPreviewSelectedUpload()
    {
        return IsValidUploadedSong(SelectedUploadedSong);
    }

    private bool CanPreviewSelectedSongSearchResult()
    {
        return SelectedSongSearchResult is not null && !string.IsNullOrWhiteSpace(SelectedSongSearchResult.AudioUrl);
    }

    private bool CanUseSelectedSongCode()
    {
        return SelectedSongSearchResult is not null;
    }

    private async Task BeginPreviewAsync(string? source, string label)
    {
        var resolvedSource = await ResolveSourceReferenceAsync(source ?? string.Empty);
        if (!Uri.TryCreate(resolvedSource, UriKind.Absolute, out var previewUri))
        {
            PreviewStatusText = "Preview needs a valid direct audio URL.";
            return;
        }

        PreviewAudioSource = null;
        PreviewHeading = string.IsNullOrWhiteSpace(label) ? previewUri.Host : label;
        PreviewStatusText = $"Loading preview for {PreviewHeading}...";
        await Task.Delay(40);
        PreviewAudioSource = previewUri;
    }

    private static bool IsValidUploadedSong(UploadedSongRecord? song)
    {
        return song is not null
            && !string.IsNullOrWhiteSpace(song.Code)
            && !string.IsNullOrWhiteSpace(song.SongName)
            && !string.IsNullOrWhiteSpace(song.Artist)
            && Uri.TryCreate(song.AudioUrl, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}
