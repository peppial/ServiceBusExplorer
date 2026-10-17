using SbExplorer.App.ViewModels;
using SbExplorer.Core.Management;
using Xunit;

namespace SbExplorer.App.Tests;

public class EntityEditorViewModelTests
{
    private readonly FakeEntityService _entities = new();

    [Theory]
    [InlineData("orders", EntityKind.Queue, null)]
    [InlineData("team/orders", EntityKind.Queue, null)]
    [InlineData("", EntityKind.Queue, "Enter a queue name.")]
    [InlineData("bad name", EntityKind.Queue, "can only contain")]
    [InlineData("/orders", EntityKind.Topic, "can't start or end with '/'")]
    [InlineData("a/b", EntityKind.Subscription, "can only contain")]
    [InlineData("$Default", EntityKind.Rule, null)]
    public void Validates_names(string name, EntityKind kind, string? expected)
    {
        var error = EntityEditorViewModel.NameError(name, kind);

        if (expected is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Contains(expected, error);
        }
    }

    [Fact]
    public void New_queue_validates_durations_and_builds_a_definition()
    {
        var vm = EntityEditorViewModel.NewQueue(_entities);
        vm.Name = "orders";
        Assert.True(vm.IsValid);

        vm.LockDuration = "10m";
        Assert.Equal("Lock duration can be at most 5 minutes.", vm.ValidationError);
        vm.LockDuration = "45s";
        vm.AutoDeleteOnIdle = "1m";
        Assert.Equal("Auto-delete on idle must be at least 5 minutes.", vm.ValidationError);
        vm.AutoDeleteOnIdle = "7d";
        vm.ForwardTo = "orders";
        Assert.Equal("An entity can't forward to itself.", vm.ValidationError);
        vm.ForwardTo = "";
        vm.DefaultMessageTimeToLive = "14d";
        vm.RequiresSession = true;

        var queue = vm.BuildQueue();

        Assert.Equal((TimeSpan.FromSeconds(45), TimeSpan.FromDays(7), TimeSpan.FromDays(14), true),
            (queue.LockDuration, queue.AutoDeleteOnIdle, queue.DefaultMessageTimeToLive, queue.RequiresSession));
        Assert.Null(queue.ForwardTo);
    }

    [Fact]
    public async Task Editing_loads_settings_and_locks_creation_only_ones()
    {
        _entities.Definitions["orders"] = new QueueDefinition
        {
            Name = "orders",
            LockDuration = TimeSpan.FromSeconds(30),
            DefaultMessageTimeToLive = TimeSpan.MaxValue,
            RequiresSession = true,
            Status = EntityStatusValue.SendDisabled,
        };

        var vm = await EntityEditorViewModel.EditAsync(_entities, FakeEntityService.Queue("orders"), TestContext.Current.CancellationToken);

        Assert.False(vm.CanChangeCreationOnlySettings);
        Assert.Equal(("30s", "Never", true, EntityStatusValue.SendDisabled),
            (vm.LockDuration, vm.DefaultMessageTimeToLive, vm.RequiresSession, vm.SelectedStatus));
        Assert.Equal("Edit queue", vm.Title);

        vm.MaxDeliveryCount = 3;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("update queue orders", _entities.Calls.Single());
        Assert.Equal(3, ((QueueDefinition)_entities.Definitions["orders"]).MaxDeliveryCount);
    }

    [Fact]
    public async Task New_subscription_can_start_with_a_filter()
    {
        var vm = EntityEditorViewModel.NewSubscription(_entities, "events");
        vm.Name = "eu";
        Assert.False(vm.ShowsRuleEditor);

        vm.UseInitialRule = true;
        vm.SqlExpression = "";
        Assert.Contains("Enter a SQL filter expression", vm.ValidationError);
        vm.SqlExpression = "region = 'eu'";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("create subscription events/eu rules=1", _entities.Calls.Single());
        Assert.Equal("events/Subscriptions/eu", vm.SavedPath);
        var rule = (RuleDefinition)_entities.Definitions["events/Subscriptions/eu/Rules/$Default"];
        Assert.Equal("region = 'eu'", rule.SqlExpression);
    }

    [Fact]
    public void Correlation_rules_need_at_least_one_criterion()
    {
        var vm = EntityEditorViewModel.NewRule(_entities, "events", "billing");
        vm.RuleName = "invoices";
        vm.SelectedFilterKind = vm.FilterKinds.Single(k => k.Kind == RuleFilterKind.Correlation);
        Assert.Contains("needs at least one", vm.ValidationError);

        vm.AddRulePropertyCommand.Execute(null);
        Assert.Equal("Every correlation property needs a name.", vm.ValidationError);
        vm.RuleProperties[0].Key = "region";
        vm.RuleProperties[0].Value = "eu";

        Assert.True(vm.IsValid);
        var rule = vm.BuildRule();
        Assert.Equal("eu", rule.Properties["region"]);
        Assert.Null(rule.SqlExpression);
    }

    [Fact]
    public async Task Save_errors_keep_the_dialog_open()
    {
        _entities.WriteFailure = new InvalidOperationException("Conflict: entity already exists");
        var vm = EntityEditorViewModel.NewTopic(_entities);
        vm.Name = "events";
        var saved = false;
        vm.Saved += (_, _) => saved = true;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(saved);
        Assert.Equal("Conflict: entity already exists", vm.SaveError);
    }
}
