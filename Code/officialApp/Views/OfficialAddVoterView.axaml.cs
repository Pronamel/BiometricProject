using Avalonia.Controls;
using officialApp.ViewModels;

namespace officialApp.Views;

public partial class OfficialAddVoterView : UserControl
{
    public OfficialAddVoterView()
    {
        InitializeComponent();

        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is OfficialAddVoterViewModel vm)
            {
                await vm.ActivateScannerAsync();
            }
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is OfficialAddVoterViewModel vm)
            {
                vm.DeactivateScanner();
            }
        };
    }
}
