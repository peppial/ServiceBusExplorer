using SbExplorer.Core.Management;

namespace SbExplorer.Core.Tests;

public class DefinitionMergeTests
{
    [Fact]
    public void Only_changed_settings_replace_the_current_ones()
    {
        var baseline = new QueueDefinition { Name = "orders", MaxDeliveryCount = 10, UserMetadata = null };
        var edited = baseline with { MaxDeliveryCount = 3, UserMetadata = "team a" };
        var current = baseline with { Status = EntityStatusValue.Disabled, ForwardTo = "elsewhere" };

        var merged = DefinitionMerge.ChangesOnto(baseline, edited, current);

        Assert.Equal(current with { MaxDeliveryCount = 3, UserMetadata = "team a" }, merged);
    }

    [Fact]
    public void Clearing_a_value_counts_as_a_change()
    {
        var baseline = new SubscriptionDefinition { Topic = "t", Name = "s", ForwardTo = "audit" };

        var merged = DefinitionMerge.ChangesOnto(baseline, baseline with { ForwardTo = null }, baseline);

        Assert.Null(merged.ForwardTo);
    }

    [Fact]
    public void Collections_come_from_the_current_entity()
    {
        var rule = new RuleDefinition { Name = "r" };
        var baseline = new SubscriptionDefinition { Topic = "t", Name = "s" };
        var current = baseline with { Rules = [rule] };

        var merged = DefinitionMerge.ChangesOnto(baseline, baseline with { Rules = [] }, current);

        Assert.Same(current.Rules, merged.Rules);
        Assert.NotSame(current, merged);
    }
}
