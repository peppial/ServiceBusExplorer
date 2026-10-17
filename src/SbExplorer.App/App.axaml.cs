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
            viewModel.LoadCommand.Execute(null);
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
        services.AddSingleton<INamespaceConnector, AzureNamespaceConnector>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
