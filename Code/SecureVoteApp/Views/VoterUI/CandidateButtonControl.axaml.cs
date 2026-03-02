using System;
using Avalonia;
using Avalonia.Controls;
using SecureVoteApp.ViewModels;

namespace SecureVoteApp.Views.VoterUI;

public partial class CandidateButtonControl : UserControl
{
    public static readonly StyledProperty<string> ButtonTextProperty =
        AvaloniaProperty.Register<CandidateButtonControl, string>(nameof(ButtonText), "Candidate Name");

    public string ButtonText
    {
        get => GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }

    public CandidateButtonControl()
    {
        InitializeComponent();
        var vm = new CandidateButtonViewModel();
        DataContext = vm;
        
        // Update ViewModel when ButtonText property changes
        PropertyChanged += (sender, args) =>
        {
            if (args.Property == ButtonTextProperty)
            {
                var text = ButtonText;
                if (!string.IsNullOrEmpty(text))
                {
                    var parts = text.Split(new[] { " - " }, 2, StringSplitOptions.None);
                    vm.CandidateName = parts.Length > 0 ? parts[0] : text;
                    vm.PartyName = parts.Length > 1 ? parts[1] : "";
                    
                    // Assign unique ID based on candidate name
                    vm.CandidateId = vm.CandidateName.GetHashCode();
                }
            }
        };
    }

    public CandidateButtonControl(int id, string name, string party)
    {
        InitializeComponent();
        DataContext = new CandidateButtonViewModel(id, name, party);
    }
}