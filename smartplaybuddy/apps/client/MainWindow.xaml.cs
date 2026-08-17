using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SmartPlayBuddy.Client.ViewModels;

namespace SmartPlayBuddy.Client;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel(DispatcherQueue);
    }
}
