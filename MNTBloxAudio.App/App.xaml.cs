using System.Windows;
using MNTBloxAudio.App.ViewModels;
using MNTBloxAudio.Core.Services;

namespace MNTBloxAudio.App;

public partial class App : Application
{
    private MainViewModel? viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsStore = new SettingsStore();
        var deviceService = new AudioDeviceService();
        var sessionService = new RobloxAudioSessionService();
        var playbackService = new ReplacementPlaybackService();
        var proxyService = new ProxyFallbackService();
        var playerLogService = new RobloxPlayerLogService();
        var soundCacheService = new RobloxSoundCacheService();
        var assetDownloadService = new RobloxAssetDownloadService();
        var replacementSourceService = new ReplacementSourceService();

        viewModel = new MainViewModel(
            settingsStore,
            deviceService,
            sessionService,
            playbackService,
            proxyService,
            playerLogService,
            soundCacheService,
            assetDownloadService,
            replacementSourceService);

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        await viewModel.InitializeAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (viewModel is not null)
        {
            await viewModel.ShutdownAsync();
        }

        base.OnExit(e);
    }
}
