namespace SbExplorer.Core.Connections;

public interface ISecretStore
{
    /// <summary>False when secrets fall back to a locally obfuscated file rather than an OS keychain.</summary>
    bool IsSecure { get; }

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string secret, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
