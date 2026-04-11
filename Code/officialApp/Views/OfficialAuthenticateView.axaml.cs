using Avalonia.Controls;
using officialApp.ViewModels;

namespace officialApp.Views;

public partial class OfficialAuthenticateView : UserControl
{
    public OfficialAuthenticateView()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is OfficialAuthenticateViewModel vm)
            {
                await vm.ActivateScannerAsync();
            }
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is OfficialAuthenticateViewModel vm)
            {
                vm.DeactivateScanner();
            }
        };
    }
}