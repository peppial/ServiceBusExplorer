using SbExplorer.Core.Connections;

namespace SbExplorer.Core.Tests;

internal sealed class InMemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Secrets { get; } = [];
    public bool IsSecure => true;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Secrets.GetValueOrDefault(key));

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        Secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Secrets.Remove(key);
        return Task.CompletedTask;
    }
}
