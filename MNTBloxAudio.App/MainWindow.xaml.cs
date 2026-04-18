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
}
