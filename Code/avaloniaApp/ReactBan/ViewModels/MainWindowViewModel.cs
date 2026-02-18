using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ReactBan.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] 
    private string greeting = "Welcome to Avalonia!";
    
    [ObservableProperty]
    private int clickCounter = 0;

    [RelayCommand] 
    private void Click()
    {
        ClickCounter++; // Increment the counter
        
        // Use if statements to react based on counter value
        if (ClickCounter == 1)
        {
            Greeting = "First click! 🎉";
        }
        else if (ClickCounter == 5)
        {
            Greeting = "You've clicked 5 times! 🔥";
        }
        else if (ClickCounter == 10)
        {
            Greeting = "Wow, 10 clicks! You're dedicated! 💪";
        }
        else if (ClickCounter % 10 == 0) // Every 10th click
        {
            Greeting = $"Milestone reached: {ClickCounter} clicks! 🚀";
        }
        else if (ClickCounter > 20)
        {
            Greeting = $"Click #{ClickCounter} - You're unstoppable! 🎯";
        }
        else
        {
            Greeting = $"Click count: {ClickCounter}";
        }
    }



    [RelayCommand]
    private void Reset()
    {
        Greeting = "Get out of here you filthy animal!";
    }

    
}
