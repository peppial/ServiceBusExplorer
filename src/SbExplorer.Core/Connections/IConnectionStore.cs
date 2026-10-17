namespace SbExplorer.Core.Connections;

public interface IConnectionStore
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces the profile. A non-null <paramref name="secret"/> replaces the stored secret.</summary>
    Task SaveAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<string?> GetSecretAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
}
