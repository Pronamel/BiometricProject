using Avalonia.Controls;

namespace officialApp.Views;

public partial class OfficialAuthenticateView : UserControl
{
    public OfficialAuthenticateView()
    {
        InitializeComponent();
        DataContext = new ViewModels.OfficialAuthenticateViewModel();
    }
}