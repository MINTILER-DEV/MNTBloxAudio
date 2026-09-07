using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MNTBloxAudio.App.ViewModels;

namespace MNTBloxAudio.App;

public partial class MainWindow : Window
{
    private bool shutdownComplete;
    private bool closing;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => { Animate(); SearchInput.Focus(); };
    }
    private void Animate()
    {
        if (SystemParameters.ClientAreaAnimation)
            BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(180)));
    }
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && IsLoaded) Animate();
    }
    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.SearchSongsCommand.CanExecute(null))
        { vm.SearchSongsCommand.Execute(null); e.Handled = true; }
    }
    private void PreviewPlayer_OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Status = "Preview unavailable. Try a different audio source.";
        PreviewPlayer.Stop();
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (shutdownComplete) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        try { if (DataContext is MainViewModel vm) await vm.ShutdownAsync(); }
        catch (Exception exception) { MessageBox.Show($"Couldn't save your library: {exception.Message}", "MNTBloxAudio"); }
        finally { shutdownComplete = true; Close(); }
    }
}
