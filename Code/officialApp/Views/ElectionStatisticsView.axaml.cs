using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace officialApp.Views;

public partial class ElectionStatisticsView : UserControl
{
    public ElectionStatisticsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
