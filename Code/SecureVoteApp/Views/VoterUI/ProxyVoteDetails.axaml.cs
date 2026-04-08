using Avalonia.Controls;
using SecureVoteApp.ViewModels;

namespace SecureVoteApp.Views.VoterUI;

public partial class ProxyVoteDetailsView : UserControl
{
    public ProxyVoteDetailsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is ProxyVoteDetailsViewModel viewModel)
            {
                viewModel.ResetSensitiveFields();
            }
        };
    }
}