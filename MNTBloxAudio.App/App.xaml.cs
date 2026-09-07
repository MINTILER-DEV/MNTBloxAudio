using System.Windows;
using MNTBloxAudio.App.ViewModels;

namespace MNTBloxAudio.App;

public partial class App : Application
{
    private Mutex? instanceMutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instanceMutex = new Mutex(true, "Local\\MNTBloxAudio", out var firstInstance);
        if (!firstInstance)
        {
            instanceMutex.Dispose();
            instanceMutex = null;
            MessageBox.Show("MNTBloxAudio is already running. Open its existing window to manage your sounds.", "MNTBloxAudio");
            Shutdown();
            return;
        }
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

    protected override void OnExit(ExitEventArgs e)
    {
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
