namespace SbExplorer.Core.Management;

/// <summary>Entities the user names directly, for credentials that can't list them.</summary>
public static class EntityReference
{
    public const string NoManageAccess = "Properties and message counts need the Manage claim, which this credential doesn't have.";

    /// <summary>
    /// Accepts <c>queue</c>, <c>topic/subscription</c> or <c>topic/Subscriptions/subscription</c>.
    /// The result is normalized to the entity's canonical path.
    /// </summary>
    public static bool TryParse(string? text, out EntityInfo? entity, out string? error)
    {
        entity = null;
        error = null;
        var parts = (text ?? string.Empty).Trim().Trim('/').Split('/', StringSplitOptions.TrimEntries);

        if (parts.Any(string.IsNullOrEmpty))
        {
            error = "Enter a queue name, or topic/subscription.";
            return false;
        }

        if (parts.Length == 1)
        {
            entity = Create(EntityKind.Queue, parts[0], parts[0]);
            return true;
        }

        if (parts.Length == 2 || (parts.Length == 3 && parts[1].Equals("Subscriptions", StringComparison.OrdinalIgnoreCase)))
        {
            var topic = parts[0];
            var subscription = parts[^1];
            entity = Create(EntityKind.Subscription, subscription, EntityInfo.SubscriptionPath(topic, subscription));
            return true;
        }

        error = "Enter a queue name, or topic/subscription.";
        return false;
    }

    private static EntityInfo Create(EntityKind kind, string name, string path) => new()
    {
        Kind = kind,
        Name = name,
        Path = path,
        Properties = [new EntityProperty("Access", "Note", NoManageAccess)],
    };
}
