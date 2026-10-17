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
        Assert.True(session.IsEmulator);
    }

    [Fact]
    public async Task Rejects_malformed_connection_string_without_network_call()
    {
        var profile = new ConnectionProfile { Name = "Bad" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AzureNamespaceConnector().ConnectAsync(profile, "nonsense", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entity_path_in_the_connection_string_does_not_lock_the_session_to_that_entity()
    {
        Emulator.SkipIfNotRunning();
        var profile = new ConnectionProfile { Name = "Emulator" };

        await using var session = await new AzureNamespaceConnector()
            .ConnectAsync(profile, Emulator.ConnectionString + "EntityPath=sbx-tests;", TestContext.Current.CancellationToken);

        Assert.True(session.CanManage);
        Assert.Equal("sbx-tests", session.ScopedEntity);
        Assert.Null(session.LimitedReason);
        // Before, the client kept EntityPath and refused every other entity.
        await session.Messages.PeekAsync(SbExplorer.Core.Messaging.MessageSource.ForQueue("orders"), 1, cancellationToken: TestContext.Current.CancellationToken);
    }
}
