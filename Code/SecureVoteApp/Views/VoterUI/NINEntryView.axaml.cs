using Avalonia.Controls;
using SecureVoteApp.ViewModels;

namespace SecureVoteApp.Views.VoterUI;

public partial class NINEntryView : UserControl
{
    public NINEntryView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is NINEntryViewModel viewModel)
            {
                viewModel.ResetSensitiveFields();
            }
        };
    }
}