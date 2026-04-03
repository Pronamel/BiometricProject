using Avalonia;
using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using SecureVoteApp.Services;
using SecureVoteApp.Services.Scanner;
using SecureVoteApp.ViewModels;

namespace SecureVoteApp;

sealed class Program
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
        
        // Register services
        services.AddSingleton<IApiService, ApiService>();
        services.AddSingleton<IVoterRealtimeService, VoterRealtimeService>();
        services.AddSingleton<IServerHandler, ServerHandler>();
        services.AddSingleton<DeviceLockState>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<CountyService>();
        services.AddSingleton<IScannerService, ScannerService>();
        
        // Register view models
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<VoterLoginViewModel>();
        services.AddSingleton<NINEntryViewModel>();
        services.AddSingleton<AuthenticateUserViewModel>();
        services.AddSingleton<PersonalOrProxyViewModel>();
        services.AddSingleton<ProxyVoteDetailsViewModel>();
        services.AddSingleton<BallotPaperViewModel>();
    }
}
