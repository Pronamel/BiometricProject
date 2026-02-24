using Avalonia.Controls;

namespace SecureVoteApp.Views.OfficialUI;

public partial class OfficialAuthenticateView : UserControl
{
    public OfficialAuthenticateView()
    {
        InitializeComponent();
        DataContext = new ViewModels.OfficialAuthenticateViewModel();
    }
}
