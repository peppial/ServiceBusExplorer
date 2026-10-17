using SbExplorer.Core.Connections;
using SbExplorer.Infrastructure.Connections;

namespace SbExplorer.IntegrationTests;

public class ConnectorTests
{
    [Fact]
    public async Task Connects_to_emulator_with_connection_string()
    {
        Emulator.SkipIfNotRunning();
        var profile = new ConnectionProfile { Name = "Emulator" };

        await using var session = await new AzureNamespaceConnector()
            .ConnectAsync(profile, Emulator.ConnectionString, TestContext.Current.CancellationToken);

        Assert.Equal("localhost", session.FullyQualifiedNamespace);
    }

    [Fact]
    public async Task Rejects_malformed_connection_string_without_network_call()
    {
        var profile = new ConnectionProfile { Name = "Bad" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AzureNamespaceConnector().ConnectAsync(profile, "nonsense", TestContext.Current.CancellationToken));
    }
}
