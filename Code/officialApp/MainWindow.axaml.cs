using Avalonia.Controls;
using officialApp.ViewModels;

namespace officialApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}