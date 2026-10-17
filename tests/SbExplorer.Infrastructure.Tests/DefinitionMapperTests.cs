using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Management;
using SbExplorer.Infrastructure.Management;

namespace SbExplorer.Infrastructure.Tests;

public class DefinitionMapperTests
{
    public static TheoryData<RuleDefinition> Rules => new()
    {
        new RuleDefinition { Name = "sql", SqlExpression = "a = 1", ActionSqlExpression = "SET b = 2" },
        new RuleDefinition { Name = "all", FilterKind = RuleFilterKind.True },
        new RuleDefinition { Name = "none", FilterKind = RuleFilterKind.False },
        new RuleDefinition
        {
            Name = "corr",
            FilterKind = RuleFilterKind.Correlation,
            Subject = "invoice",
            CorrelationId = "c1",
            ContentType = "application/json",
            Properties = new Dictionary<string, string> { ["region"] = "eu" },
        },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Rules_round_trip_through_sdk_models(RuleDefinition rule)
    {
        var sdk = ServiceBusModelFactory.RuleProperties(rule.Name, DefinitionMapper.ToFilter(rule), DefinitionMapper.ToAction(rule));

        var back = DefinitionMapper.ToDefinition(sdk);

        Assert.Equal(rule.FilterKind, back.FilterKind);
        Assert.Equal(rule.SqlExpression, back.SqlExpression);
        Assert.Equal(rule.Subject, back.Subject);
        Assert.Equal(rule.CorrelationId, back.CorrelationId);
        Assert.Equal(rule.ActionSqlExpression, back.ActionSqlExpression);
        Assert.Equal(rule.Properties, back.Properties);
    }

    [Theory]
    [InlineData(EntityStatusValue.Active)]
    [InlineData(EntityStatusValue.Disabled)]
    [InlineData(EntityStatusValue.SendDisabled)]
    [InlineData(EntityStatusValue.ReceiveDisabled)]
    public void Status_round_trips(EntityStatusValue status) =>
        Assert.Equal(status, DefinitionMapper.FromStatus(DefinitionMapper.ToStatus(status)));

    [Fact]
    public void Queue_create_options_only_set_the_duplicate_window_when_enabled()
    {
        var withoutDedup = DefinitionMapper.ToCreateOptions(new QueueDefinition { Name = "q", DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(3) });
        var withDedup = DefinitionMapper.ToCreateOptions(new QueueDefinition
        {
            Name = "q",
            RequiresDuplicateDetection = true,
            DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(3),
            ForwardTo = "  ",
        });

        Assert.NotEqual(TimeSpan.FromMinutes(3), withoutDedup.DuplicateDetectionHistoryTimeWindow);
        Assert.Equal(TimeSpan.FromMinutes(3), withDedup.DuplicateDetectionHistoryTimeWindow);
        Assert.Null(withDedup.ForwardTo);
    }
}
