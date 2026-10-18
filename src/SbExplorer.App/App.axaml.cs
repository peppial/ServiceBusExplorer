using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.App.Views;
using SbExplorer.Core;
using SbExplorer.Core.Connections;
using SbExplorer.Infrastructure.Connections;
using SbExplorer.Infrastructure.Secrets;

namespace SbExplorer.App;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += async (_, _) => await viewModel.DisconnectCommand.ExecuteAsync(null);

            // Like the original, start with empty panels and go straight to "Connect to a Service Bus Namespace".
            desktop.MainWindow.Opened += async (_, _) =>
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
                if (!viewModel.IsConnected)
                {
                    await viewModel.ConnectCommand.ExecuteAsync(null);
                }
            };
            Services.GetRequiredService<ILogService>().Info("Service Bus Explorer started. Use File > Connect to open a namespace.");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ => SecretStoreFactory.CreateDefault(AppPaths.DataDirectory));
        services.AddSingleton<IConnectionStore>(sp =>
            new JsonConnectionStore(AppPaths.ConnectionsFile, sp.GetRequiredService<ISecretStore>()));
        services.AddSingleton<INamespaceConnector>(sp => new AzureNamespaceConnector(async (info, _) =>
        {
            sp.GetRequiredService<ILogService>().Info(info.Message);
            await sp.GetRequiredService<IDialogService>().ShowDeviceCodeAsync(info.Message, info.UserCode, info.VerificationUri);
        }));
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
