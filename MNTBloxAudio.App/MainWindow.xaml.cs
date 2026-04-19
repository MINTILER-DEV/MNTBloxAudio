using System.Windows;
using MNTBloxAudio.App.ViewModels;

namespace MNTBloxAudio.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void PreviewPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        ViewModel?.HandlePreviewPlaybackStarted();
    }

    private void PreviewPlayer_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        ViewModel?.HandlePreviewPlaybackFinished();
    }

    private void PreviewPlayer_OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ViewModel?.HandlePreviewPlaybackFailed(e.ErrorException?.Message ?? "Unknown media error.");
        PreviewPlayer.Stop();
    }
}
