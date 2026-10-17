using SbExplorer.App.ViewModels;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

/// <summary>UI guards that keep the explorer from disturbing a live namespace by accident.</summary>
public class ProductionSafetyGuardTests
{
    private readonly FakeMessageService _service = new();
    private readonly FakeDialogs _dialogs = new();
    private static readonly EntityInfo Orders = new() { Kind = EntityKind.Queue, Name = "orders", Path = "orders", MaxDeliveryCount = 3 };

    [Fact]
    public async Task Delete_selected_is_only_offered_in_dead_letter_queues()
    {
        _service.Seed("orders", 2);
        var active = new MessagesViewModel(_service, _dialogs, Orders);
        await active.PeekCommand.ExecuteAsync(null);
        active.SelectedRows = active.Messages.ToList();
        Assert.False(active.DeleteCommand.CanExecute(null));

        _service.Seed("orders/$DeadLetterQueue", 1);
        var deadLetters = new MessagesViewModel(_service, _dialogs, Orders, fixedSubQueue: SubQueueKind.DeadLetter);
        await deadLetters.PeekCommand.ExecuteAsync(null);
        Assert.True(deadLetters.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Edit_and_resubmit_keeps_the_original_when_the_copy_reuses_its_message_id()
    {
        _service.Seed("orders/$DeadLetterQueue", 1, _ => new MessageInfo { MessageId = "order-7", Body = "{}"u8.ToArray() });
        var vm = new MessagesViewModel(_service, _dialogs, Orders, fixedSubQueue: SubQueueKind.DeadLetter);
        await vm.PeekCommand.ExecuteAsync(null);
        _dialogs.OnSend = async sender =>
        {
            sender.MessageId = "order-7";
            await sender.SendCommand.ExecuteAsync(null);
            return true;
        };

        await vm.EditAndResubmitCommand.ExecuteAsync(null);

        Assert.Single(_service.Sent);
        Assert.Single(_service.Messages("orders/$DeadLetterQueue"));
        Assert.Contains("was kept", vm.Status);
    }

    [Fact]
    public void Lock_banner_explains_delivery_counts()
    {
        var vm = new MessagesViewModel(_service, _dialogs, Orders);

        Assert.Contains("after 3 deliveries Service Bus dead-letters the message", vm.LockWarning);
    }

    [Fact]
    public void Retrieve_dialog_warns_about_receive_modes()
    {
        var options = new ReceiveOptionsViewModel("x", canReceive: true, knownCount: null, maxDeliveryCount: 3);
        Assert.Null(options.ModeWarning);

        options.Mode = RetrieveMode.ReceiveLocked;
        Assert.Contains("after 3 the message is dead-lettered", options.ModeWarning);

        options.Mode = RetrieveMode.ReceiveAndDelete;
        Assert.Contains("removes the messages for good", options.ModeWarning);
    }

    [Fact]
    public void Resubmit_copies_get_a_new_message_id_and_remember_the_old_one()
    {
        var copy = OutgoingMessage.ResubmitCopyOf(new MessageInfo
        {
            MessageId = "order-7",
            ApplicationProperties = new Dictionary<string, object?> { ["DeadLetterReason"] = "Bad", ["tier"] = 5 },
        });

        Assert.NotEqual("order-7", copy.MessageId);
        Assert.Equal("order-7", copy.ApplicationProperties[OutgoingMessage.OriginalMessageIdProperty]);
        Assert.Equal(5, copy.ApplicationProperties["tier"]);
        Assert.False(copy.ApplicationProperties.ContainsKey("DeadLetterReason"));
    }

    [Fact]
    public async Task Rule_editor_keeps_property_types_and_parameters_it_does_not_show()
    {
        var entities = new FakeEntityService();
        entities.Definitions["t/Subscriptions/s/Rules/r"] = new RuleDefinition
        {
            Name = "r",
            FilterKind = RuleFilterKind.Sql,
            SqlExpression = "amount > @min",
            SqlParameters = new Dictionary<string, TypedValue> { ["@min"] = new("100", PropertyValueType.Int32) },
        };
        var rule = new EntityInfo { Kind = EntityKind.Rule, Name = "r", Path = "t/Subscriptions/s/Rules/r" };

        var editor = await EntityEditorViewModel.EditAsync(entities, rule, TestContext.Current.CancellationToken);

        Assert.Contains("@min = 100 (Int32)", editor.ParametersNote);
        Assert.Equal(new TypedValue("100", PropertyValueType.Int32), editor.BuildRule().SqlParameters["@min"]);

        editor.SelectedFilterKind = editor.FilterKinds.Single(k => k.Kind == RuleFilterKind.Correlation);
        editor.AddRulePropertyCommand.Execute(null);
        editor.RuleProperties[0].Key = "tier";
        editor.RuleProperties[0].Value = "five";
        editor.RuleProperties[0].Type = PropertyValueType.Int32;
        Assert.Equal("tier: 'five' is not a valid Int32.", editor.ValidationError);
        editor.RuleProperties[0].Value = "5";
        Assert.Equal(PropertyValueType.Int32, editor.BuildRule().PropertyTypes["tier"]);
    }
}
