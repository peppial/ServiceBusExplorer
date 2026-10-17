using SbExplorer.Core.Connections;
using SbExplorer.Infrastructure.Secrets;

namespace SbExplorer.Infrastructure.Tests;

public sealed class SecretStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sbx-secrets-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task File_store_round_trips_and_does_not_write_plaintext()
    {
        var store = new FileSecretStore(_directory);

        await AssertRoundTrip(store);

        var onDisk = await File.ReadAllTextAsync(Path.Combine(_directory, "secrets.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("hunter2", onDisk);
    }

    [Fact]
    public async Task File_store_returns_null_when_key_file_is_lost()
    {
        var store = new FileSecretStore(_directory);
        await store.SetAsync("k", "hunter2", TestContext.Current.CancellationToken);

        File.Delete(Path.Combine(_directory, "secrets.key"));

        Assert.Null(await new FileSecretStore(_directory).GetAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Windows_dpapi_store_round_trips()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("DPAPI is Windows only.");
            return;
        }

        await AssertRoundTrip(new WindowsDpapiSecretStore(_directory));
    }

    [Fact]
    public async Task Mac_keychain_store_round_trips()
    {
        // Touches the real login keychain, so it only runs when asked for.
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("SBX_KEYCHAIN_TESTS") != "1")
        {
            Assert.Skip("Set SBX_KEYCHAIN_TESTS=1 on macOS to run.");
            return;
        }

        await AssertRoundTrip(new MacKeychainSecretStore("ServiceBusExplorer.Tests"));
    }

    private static async Task AssertRoundTrip(ISecretStore store)
    {
        var ct = TestContext.Current.CancellationToken;
        var key = $"test:{Guid.NewGuid():N}";

        Assert.Null(await store.GetAsync(key, ct));

        await store.SetAsync(key, "hunter2", ct);
        Assert.Equal("hunter2", await store.GetAsync(key, ct));

        await store.SetAsync(key, "correct horse", ct);
        Assert.Equal("correct horse", await store.GetAsync(key, ct));

        await store.DeleteAsync(key, ct);
        Assert.Null(await store.GetAsync(key, ct));
        await store.DeleteAsync(key, ct);
    }
}
