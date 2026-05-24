using System.Windows;
using System.Windows.Media;
using MNTBloxAudio.App.ViewModels;
using MNTBloxAudio.Core.Models;
using MNTBloxAudio.Core.Services;

namespace MNTBloxAudio.App;

public partial class App : Application
{
    private static readonly IReadOnlyDictionary<string, Color> LightTheme = new Dictionary<string, Color>
    {
        ["WindowBrush"] = (Color)ColorConverter.ConvertFromString("#3A3837")!,
        ["CardBrush"] = (Color)ColorConverter.ConvertFromString("#333130")!,
        ["CardBorderBrush"] = (Color)ColorConverter.ConvertFromString("#292726")!,
        ["SurfaceBrush"] = (Color)ColorConverter.ConvertFromString("#1E1C1B")!,
        ["SurfaceAltBrush"] = (Color)ColorConverter.ConvertFromString("#373433")!,
        ["InputBrush"] = (Color)ColorConverter.ConvertFromString("#1D1B1A")!,
        ["TextBrush"] = (Color)ColorConverter.ConvertFromString("#757270")!,
        ["BrightTextBrush"] = (Color)ColorConverter.ConvertFromString("#D3D0CC")!,
        ["MutedTextBrush"] = (Color)ColorConverter.ConvertFromString("#676360")!,
        ["AccentBrush"] = (Color)ColorConverter.ConvertFromString("#89EAF7")!,
        ["AccentTextBrush"] = (Color)ColorConverter.ConvertFromString("#171717")!,
        ["SubtleBrush"] = (Color)ColorConverter.ConvertFromString("#262322")!,
        ["ReadyBrush"] = (Color)ColorConverter.ConvertFromString("#E6BE63")!,
        ["InactiveBrush"] = (Color)ColorConverter.ConvertFromString("#45413F")!,
        ["StandbyBrush"] = (Color)ColorConverter.ConvertFromString("#7F7A74")!,
    };

    private static readonly IReadOnlyDictionary<string, Color> DarkTheme = new Dictionary<string, Color>
    {
        ["WindowBrush"] = (Color)ColorConverter.ConvertFromString("#3A3837")!,
        ["CardBrush"] = (Color)ColorConverter.ConvertFromString("#333130")!,
        ["CardBorderBrush"] = (Color)ColorConverter.ConvertFromString("#292726")!,
        ["SurfaceBrush"] = (Color)ColorConverter.ConvertFromString("#1E1C1B")!,
        ["SurfaceAltBrush"] = (Color)ColorConverter.ConvertFromString("#373433")!,
        ["InputBrush"] = (Color)ColorConverter.ConvertFromString("#1D1B1A")!,
        ["TextBrush"] = (Color)ColorConverter.ConvertFromString("#757270")!,
        ["BrightTextBrush"] = (Color)ColorConverter.ConvertFromString("#D3D0CC")!,
        ["MutedTextBrush"] = (Color)ColorConverter.ConvertFromString("#676360")!,
        ["AccentBrush"] = (Color)ColorConverter.ConvertFromString("#89EAF7")!,
        ["AccentTextBrush"] = (Color)ColorConverter.ConvertFromString("#171717")!,
        ["SubtleBrush"] = (Color)ColorConverter.ConvertFromString("#262322")!,
        ["ReadyBrush"] = (Color)ColorConverter.ConvertFromString("#E6BE63")!,
        ["InactiveBrush"] = (Color)ColorConverter.ConvertFromString("#45413F")!,
        ["StandbyBrush"] = (Color)ColorConverter.ConvertFromString("#7F7A74")!,
    };

    private MainViewModel? viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsStore = new SettingsStore();
        var startupSettings = await settingsStore.LoadAsync();
        ApplyTheme(startupSettings.UseDarkMode);

        var deviceService = new AudioDeviceService();
        var sessionService = new RobloxAudioSessionService();
        var playbackService = new ReplacementPlaybackService();
        var proxyService = new ProxyFallbackService();
        var playerLogService = new RobloxPlayerLogService();
        var soundCacheService = new RobloxSoundCacheService();
        var assetDownloadService = new RobloxAssetDownloadService();
        var replacementSourceService = new ReplacementSourceService();
        var songIndexService = new SongIndexService();

        viewModel = new MainViewModel(
            settingsStore,
            deviceService,
            sessionService,
            playbackService,
            proxyService,
            playerLogService,
            soundCacheService,
            assetDownloadService,
            replacementSourceService,
            songIndexService);

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

    public void ApplyTheme(bool useDarkMode)
    {
        ApplyThemePalette(useDarkMode ? DarkTheme : LightTheme);
    }

    private void ApplyThemePalette(IReadOnlyDictionary<string, Color> palette)
    {
        foreach (var entry in palette)
        {
            if (Resources[entry.Key] is SolidColorBrush brush)
            {
                if (brush.IsFrozen)
                {
                    var writableBrush = brush.CloneCurrentValue();
                    writableBrush.Color = entry.Value;
                    Resources[entry.Key] = writableBrush;
                    continue;
                }

                brush.Color = entry.Value;
            }
        }
    }
}
