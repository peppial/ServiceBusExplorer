namespace SbExplorer.Core.Management;

public interface IEntityService
{
    Task<IReadOnlyList<EntityInfo>> GetQueuesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntityInfo>> GetTopicsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntityInfo>> GetSubscriptionsAsync(string topic, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntityInfo>> GetRulesAsync(string topic, string subscription, CancellationToken cancellationToken = default);
}
