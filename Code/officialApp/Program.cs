using System;
using System.Net.Http;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using officialApp.Services;
using officialApp.Services.Scanner;
using officialApp.ViewModels;

namespace officialApp;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static void ConfigureServices(IServiceCollection services)
    {
        // Register HttpClient
        services.AddSingleton(new HttpClient());
        
        // Register navigation service
        services.AddSingleton<INavigationService, NavigationService>();
        
        // Register data services
        services.AddSingleton<IApiService, ApiService>();
        services.AddSingleton<IServerHandler, ServerHandler>();
        services.AddSingleton<IScannerService, ScannerService>();
        
        // Register ViewModels
        services.AddSingleton<OfficialLoginViewModel>();
        services.AddSingleton<OfficialAuthenticateViewModel>();
        services.AddSingleton<OfficialMenuViewModel>();
        services.AddSingleton<OfficialGenerateAccessCodeViewModel>();
        services.AddSingleton<OfficialVotingPollingManagerViewModel>();
        services.AddSingleton<MainWindowViewModel>();
    }
}
