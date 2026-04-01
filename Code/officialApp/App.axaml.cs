using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using officialApp.Services;
using officialApp.ViewModels;

namespace officialApp;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; set; }
    private bool _shutdownHandled;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Set up dependency injection
        var services = new ServiceCollection();
        Program.ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Add global exception handler
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = (Exception)e.ExceptionObject;
                Console.WriteLine($"[FATAL] AppDomain.UnhandledException: {ex.GetType().Name}");
                Console.WriteLine($"[FATAL] Message: {ex.Message}");
                Console.WriteLine($"[FATAL] Stack trace: {ex.StackTrace}");
            };
            
            // Get MainWindow and set DataContext to MainWindowViewModel
            var mainWindow = new MainWindow();
            var mainViewModel = ServiceProvider.GetRequiredService<MainWindowViewModel>();
            mainWindow.DataContext = mainViewModel;

            EventHandler<WindowClosingEventArgs>? onWindowClosing = null;
            onWindowClosing = async (_, e) =>
            {
                if (_shutdownHandled)
                {
                    return;
                }

                _shutdownHandled = true;
                e.Cancel = true;

                try
                {
                    var apiService = ServiceProvider?.GetService<IApiService>();
                    if (apiService?.IsAuthenticated == true)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] App is closing - logging out official session...");
                        await apiService.LogoutAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error during shutdown logout: {ex.Message}");
                }
                finally
                {
                    if (onWindowClosing != null)
                    {
                        mainWindow.Closing -= onWindowClosing;
                    }

                    desktop.Shutdown();
                }
            };

            mainWindow.Closing += onWindowClosing;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}