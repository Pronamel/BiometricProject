using Avalonia.Controls;

namespace SecureVoteApp.Views.VoterUI;

public partial class AuthenticateUserView : UserControl
{
    public AuthenticateUserView()
    {
        InitializeComponent();
        DataContext = new ViewModels.AuthenticateUserViewModel();
    }
}
