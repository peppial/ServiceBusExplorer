using SbExplorer.Core.Management;

namespace SbExplorer.Core.Tests;

public class EntityReferenceTests
{
    [Theory]
    [InlineData("orders", EntityKind.Queue, "orders", "orders")]
    [InlineData(" /orders/ ", EntityKind.Queue, "orders", "orders")]
    [InlineData("events/billing", EntityKind.Subscription, "billing", "events/Subscriptions/billing")]
    [InlineData("events/subscriptions/billing", EntityKind.Subscription, "billing", "events/Subscriptions/billing")]
    public void Parses_queue_and_subscription_names(string text, EntityKind kind, string name, string path)
    {
        Assert.True(EntityReference.TryParse(text, out var entity, out var error), error);
        Assert.Equal((kind, name, path), (entity!.Kind, entity.Name, entity.Path));
        Assert.Null(entity.Counts);
        Assert.Contains(entity.Properties, p => p.Value == EntityReference.NoManageAccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a//b")]
    [InlineData("a/b/c")]
    [InlineData("a/Subscriptions/b/c")]
    public void Rejects_other_shapes(string text)
    {
        Assert.False(EntityReference.TryParse(text, out _, out var error));
        Assert.Equal("Enter a queue name, or topic/subscription.", error);
    }
}
