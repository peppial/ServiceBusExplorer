using SbExplorer.Core.Connections;

namespace SbExplorer.Core.Tests;

public sealed class JsonConnectionStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sbx-tests-").FullName;
    private readonly InMemorySecretStore _secrets = new();

    private string FilePath => Path.Combine(_directory, "connections.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Round_trips_profiles_and_keeps_secrets_out_of_the_file()
    {
        var store = new JsonConnectionStore(FilePath, _secrets);
        var profile = new ConnectionProfile { Name = "Prod", Auth = AuthKind.ConnectionString, PinnedEntities = ["orders", "events/Subscriptions/x"] };

        await store.SaveAsync(profile, "Endpoint=sb://x/;SharedAccessKeyName=k;SharedAccessKey=TOP-SECRET", TestContext.Current.CancellationToken);

        var reloaded = await new JsonConnectionStore(FilePath, _secrets).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(profile, Assert.Single(reloaded));
        Assert.DoesNotContain("TOP-SECRET", await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken));
        Assert.Contains("TOP-SECRET", await store.GetSecretAsync(profile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saving_existing_profile_replaces_it_and_null_secret_keeps_old_one()
    {
        var store = new JsonConnectionStore(FilePath, _secrets);
        var profile = new ConnectionProfile { Name = "Dev" };
        await store.SaveAsync(profile, "secret-1", TestContext.Current.CancellationToken);

        await store.SaveAsync(profile with { Name = "Dev renamed" }, secret: null, TestContext.Current.CancellationToken);

        var saved = Assert.Single(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Dev renamed", saved.Name);
        Assert.Equal("secret-1", await store.GetSecretAsync(saved, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Switching_to_entra_drops_the_stored_connection_string()
    {
        var store = new JsonConnectionStore(FilePath, _secrets);
        var profile = new ConnectionProfile { Name = "Dev" };
        await store.SaveAsync(profile, "secret-1", TestContext.Current.CancellationToken);

        await store.SaveAsync(
            profile with { Auth = AuthKind.Entra, FullyQualifiedNamespace = "x.servicebus.windows.net" },
            secret: null,
            TestContext.Current.CancellationToken);

        Assert.Empty(_secrets.Secrets);
    }

    [Fact]
    public async Task Delete_removes_profile_and_secret()
    {
        var store = new JsonConnectionStore(FilePath, _secrets);
        var profile = new ConnectionProfile { Name = "Dev" };
        await store.SaveAsync(profile, "secret", TestContext.Current.CancellationToken);

        await store.DeleteAsync(profile.Id, TestContext.Current.CancellationToken);

        Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_secrets.Secrets);
    }

    [Fact]
    public async Task Missing_file_loads_as_empty()
    {
        var store = new JsonConnectionStore(Path.Combine(_directory, "nope", "connections.json"), _secrets);

        Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));
    }
}
