using Avalonia;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureVoteApp.Services;
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
        services.AddHttpClient<IApiService, ApiService>();
        
        // Register API services
        services.AddTransient<IApiTestService, ApiTestService>();
        
        // Register other services
        services.AddSingleton<INavigationService, NavigationService>();
        
        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<BallotPaperViewModel>();
        services.AddTransient<AuthenticateUserViewModel>();
        services.AddTransient<NINEntryViewModel>();
        services.AddTransient<PersonalOrProxyViewModel>();
        services.AddTransient<ProxyVoteDetailsViewModel>();
    }
}
