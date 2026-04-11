using Avalonia.Controls;
using SecureVoteApp.ViewModels;

namespace SecureVoteApp.Views.VoterUI;

public partial class AuthenticateUserView : UserControl
{
    public AuthenticateUserView()
    {
        InitializeComponent();

        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is AuthenticateUserViewModel viewModel)
            {
                viewModel.ResetAuthenticationState();
                viewModel.ApplyPendingLookup();
                await viewModel.ActivateScannerAsync();
            }
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is AuthenticateUserViewModel viewModel)
            {
                viewModel.DeactivateScanner();
            }
        };
    }
}
