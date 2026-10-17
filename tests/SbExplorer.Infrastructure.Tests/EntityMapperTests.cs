using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Management;
using SbExplorer.Infrastructure.Management;

namespace SbExplorer.Infrastructure.Tests;

public class EntityMapperTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("audit", "audit")]
    [InlineData("sb://contoso.servicebus.windows.net/audit", "audit")]
    [InlineData("https://contoso.servicebus.windows.net/events/subscriptions/x", "events/subscriptions/x")]
    public void Entity_name_strips_namespace_uri(string? forwardTo, string? expected) =>
        Assert.Equal(expected, EntityMapper.EntityName(forwardTo));

    [Fact]
    public void Maps_sql_rule_with_action()
    {
        var rule = new CreateRuleOptions("priority-high", new SqlRuleFilter("priority = 'high'"))
        {
            Action = new SqlRuleAction("SET escalated = 'true'"),
        };

        var info = EntityMapper.Rule("events", "high-priority", ServiceBusModelFactory.RuleProperties(rule.Name, rule.Filter, rule.Action));

        Assert.Equal(EntityKind.Rule, info.Kind);
        Assert.Equal("events/Subscriptions/high-priority/Rules/priority-high", info.Path);
        Assert.Contains(info.Properties, p => p is { Name: "Type", Value: "SQL" });
        Assert.Contains(info.Properties, p => p is { Name: "Expression", Value: "priority = 'high'" });
        Assert.Contains(info.Properties, p => p is { Name: "SQL action", Value: "SET escalated = 'true'" });
    }

    [Fact]
    public void Maps_correlation_rule_with_only_set_fields()
    {
        var filter = new CorrelationRuleFilter { Subject = "invoice", ApplicationProperties = { ["region"] = "eu" } };

        var info = EntityMapper.Rule("events", "billing", ServiceBusModelFactory.RuleProperties("invoices", filter, null));

        Assert.Equal(
            ["Type", "Subject", "Property: region", "SQL action"],
            info.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Maps_true_filter_default_rule()
    {
        var info = EntityMapper.Rule("t", "s", ServiceBusModelFactory.RuleProperties("$Default", new TrueRuleFilter(), null));

        Assert.Contains(info.Properties, p => p is { Name: "Type", Value: "True (all messages)" });
    }
}
