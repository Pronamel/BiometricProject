using Avalonia.Controls;
using officialApp.ViewModels;

namespace officialApp.Views;

public partial class OfficialAssignProxyView : UserControl
{
    public OfficialAssignProxyView()
    {
        InitializeComponent();

        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is OfficialAssignProxyViewModel vm)
            {
                await vm.ActivateScannerAsync();
            }
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is OfficialAssignProxyViewModel vm)
            {
                vm.DeactivateScanner();
            }
        };
    }
}