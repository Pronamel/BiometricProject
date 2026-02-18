using ReactiveUI;
using System.Windows.Input;

namespace TestApp.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private string _greeting = "Welcome to Avalonia!";
    
    public string Greeting
    {
        get => _greeting;
        set => this.RaiseAndSetIfChanged(ref _greeting, value);
    }
    
    public ICommand OnButtonClickCommand => new SimpleCommand(() => 
    {
        Greeting = "I hate rocket league!";
    });
}

public class SimpleCommand : ICommand
{
    private readonly System.Action _execute;
    
    public event System.EventHandler? CanExecuteChanged { add { } remove { } }
    
    public SimpleCommand(System.Action execute) => _execute = execute;
    
    public bool CanExecute(object? parameter) => true;
    
    public void Execute(object? parameter) => _execute();
}
