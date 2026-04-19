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
        ["WindowBrush"] = (Color)ColorConverter.ConvertFromString("#F7F8FA")!,
        ["CardBrush"] = (Color)ColorConverter.ConvertFromString("#FFFFFF")!,
        ["CardBorderBrush"] = (Color)ColorConverter.ConvertFromString("#E6E8EC")!,
        ["SurfaceBrush"] = (Color)ColorConverter.ConvertFromString("#F7F8FA")!,
        ["SurfaceAltBrush"] = (Color)ColorConverter.ConvertFromString("#FAFBFC")!,
        ["InputBrush"] = (Color)ColorConverter.ConvertFromString("#FFFFFF")!,
        ["TextBrush"] = (Color)ColorConverter.ConvertFromString("#171A21")!,
        ["MutedTextBrush"] = (Color)ColorConverter.ConvertFromString("#6B7280")!,
        ["AccentBrush"] = (Color)ColorConverter.ConvertFromString("#4B5563")!,
        ["AccentTextBrush"] = (Color)ColorConverter.ConvertFromString("#FFFFFF")!,
        ["SubtleBrush"] = (Color)ColorConverter.ConvertFromString("#F2F4F7")!,
    };

    private static readonly IReadOnlyDictionary<string, Color> DarkTheme = new Dictionary<string, Color>
    {
        ["WindowBrush"] = (Color)ColorConverter.ConvertFromString("#0B0E13")!,
        ["CardBrush"] = (Color)ColorConverter.ConvertFromString("#141922")!,
        ["CardBorderBrush"] = (Color)ColorConverter.ConvertFromString("#2A3240")!,
        ["SurfaceBrush"] = (Color)ColorConverter.ConvertFromString("#121721")!,
        ["SurfaceAltBrush"] = (Color)ColorConverter.ConvertFromString("#171D28")!,
        ["InputBrush"] = (Color)ColorConverter.ConvertFromString("#0F141C")!,
        ["TextBrush"] = (Color)ColorConverter.ConvertFromString("#EEF3FB")!,
        ["MutedTextBrush"] = (Color)ColorConverter.ConvertFromString("#9AA8BD")!,
        ["AccentBrush"] = (Color)ColorConverter.ConvertFromString("#4FD3B3")!,
        ["AccentTextBrush"] = (Color)ColorConverter.ConvertFromString("#08120F")!,
        ["SubtleBrush"] = (Color)ColorConverter.ConvertFromString("#1A2230")!,
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
