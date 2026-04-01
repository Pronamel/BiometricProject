using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using SecureVoteApp.ViewModels;
using SecureVoteApp.Views.VoterUI;
using Microsoft.Extensions.DependencyInjection;
using System;
using SecureVoteApp.Services;
using Avalonia.Controls;

namespace SecureVoteApp;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private bool _shutdownHandled;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Setup dependency injection
            var services = new ServiceCollection();
            Program.ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };

            if (desktop.MainWindow is Window mainWindow)
            {
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
                        var apiService = _serviceProvider?.GetService<IApiService>();
                        if (apiService?.IsAuthenticated == true)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] App is closing - logging out voter session...");
                            await apiService.LogoutAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error during SecureVote shutdown logout: {ex.Message}");
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
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}