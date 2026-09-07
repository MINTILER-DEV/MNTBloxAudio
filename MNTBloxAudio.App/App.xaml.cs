using System.Windows;
using MNTBloxAudio.App.ViewModels;

namespace MNTBloxAudio.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
            await viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"MNTBloxAudio couldn't start: {exception.Message}", "MNTBloxAudio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
