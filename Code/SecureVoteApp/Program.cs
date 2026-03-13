using Avalonia;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SecureVoteApp.Services;
using SecureVoteApp.ViewModels;
using SecureVoteApp.Data;

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
        
        // Register API and Server Handler services
        services.AddSingleton<IApiService, ApiService>();
        services.AddSingleton<IServerHandler, ServerHandler>();
        
        // Register other services
        services.AddSingleton<INavigationService, NavigationService>();
        
        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<BallotPaperViewModel>();
        services.AddTransient<AuthenticateUserViewModel>();
        services.AddTransient<NINEntryViewModel>();
        services.AddTransient<PersonalOrProxyViewModel>();
        services.AddTransient<ProxyVoteDetailsViewModel>();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
    }
}
