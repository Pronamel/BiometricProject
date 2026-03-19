using Avalonia.Controls;
using officialApp.ViewModels;

namespace officialApp.Views;

public partial class OfficialGenerateAccessCodeView : UserControl
{
    public OfficialGenerateAccessCodeView()
    {
        InitializeComponent();
        DataContext = new OfficialGenerateAccessCodeViewModel();
    }
}
