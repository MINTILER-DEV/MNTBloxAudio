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
        ["WindowBrush"] = (Color)ColorConverter.ConvertFromString("#ECE7E0")!,
        ["CardBrush"] = (Color)ColorConverter.ConvertFromString("#F5F1EB")!,
        ["CardBorderBrush"] = (Color)ColorConverter.ConvertFromString("#C6BEB4")!,
        ["SurfaceBrush"] = (Color)ColorConverter.ConvertFromString("#E3DBD1")!,
        ["SurfaceAltBrush"] = (Color)ColorConverter.ConvertFromString("#EFE8DF")!,
        ["InputBrush"] = (Color)ColorConverter.ConvertFromString("#DDD4C9")!,
        ["TextBrush"] = (Color)ColorConverter.ConvertFromString("#1E1B18")!,
        ["MutedTextBrush"] = (Color)ColorConverter.ConvertFromString("#655E56")!,
        ["AccentBrush"] = (Color)ColorConverter.ConvertFromString("#58BFCB")!,
        ["AccentTextBrush"] = (Color)ColorConverter.ConvertFromString("#091114")!,
        ["SubtleBrush"] = (Color)ColorConverter.ConvertFromString("#D6CDC2")!,
        ["SelectionBrush"] = (Color)ColorConverter.ConvertFromString("#58BFCB")!,
        ["SuccessBrush"] = (Color)ColorConverter.ConvertFromString("#D5F0F0")!,
        ["SuccessTextBrush"] = (Color)ColorConverter.ConvertFromString("#154348")!,
        ["InfoBrush"] = (Color)ColorConverter.ConvertFromString("#D7E7F0")!,
        ["InfoTextBrush"] = (Color)ColorConverter.ConvertFromString("#1F4A5D")!,
        ["WarningBrush"] = (Color)ColorConverter.ConvertFromString("#F1DCCF")!,
        ["WarningTextBrush"] = (Color)ColorConverter.ConvertFromString("#6A3C28")!,
        ["MutedBadgeBrush"] = (Color)ColorConverter.ConvertFromString("#DDD4C9")!,
        ["MutedBadgeTextBrush"] = (Color)ColorConverter.ConvertFromString("#635A51")!,
    };

    private static readonly IReadOnlyDictionary<string, Color> DarkTheme = new Dictionary<string, Color>
    {
        ["WindowBrush"] = (Color)ColorConverter.ConvertFromString("#353332")!,
        ["CardBrush"] = (Color)ColorConverter.ConvertFromString("#2F2D2C")!,
        ["CardBorderBrush"] = (Color)ColorConverter.ConvertFromString("#3B3938")!,
        ["SurfaceBrush"] = (Color)ColorConverter.ConvertFromString("#262423")!,
        ["SurfaceAltBrush"] = (Color)ColorConverter.ConvertFromString("#332F2E")!,
        ["InputBrush"] = (Color)ColorConverter.ConvertFromString("#1F1D1C")!,
        ["TextBrush"] = (Color)ColorConverter.ConvertFromString("#E7E4DF")!,
        ["MutedTextBrush"] = (Color)ColorConverter.ConvertFromString("#8B8781")!,
        ["AccentBrush"] = (Color)ColorConverter.ConvertFromString("#88E8F2")!,
        ["AccentTextBrush"] = (Color)ColorConverter.ConvertFromString("#0F1A1D")!,
        ["SubtleBrush"] = (Color)ColorConverter.ConvertFromString("#252322")!,
        ["SelectionBrush"] = (Color)ColorConverter.ConvertFromString("#88E8F2")!,
        ["SuccessBrush"] = (Color)ColorConverter.ConvertFromString("#16373C")!,
        ["SuccessTextBrush"] = (Color)ColorConverter.ConvertFromString("#C9FCFF")!,
        ["InfoBrush"] = (Color)ColorConverter.ConvertFromString("#21343B")!,
        ["InfoTextBrush"] = (Color)ColorConverter.ConvertFromString("#CBEFFF")!,
        ["WarningBrush"] = (Color)ColorConverter.ConvertFromString("#4B3830")!,
        ["WarningTextBrush"] = (Color)ColorConverter.ConvertFromString("#FFD8C6")!,
        ["MutedBadgeBrush"] = (Color)ColorConverter.ConvertFromString("#2A2827")!,
        ["MutedBadgeTextBrush"] = (Color)ColorConverter.ConvertFromString("#BBB4AC")!,
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
