using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

public class EntityPanelViewModelTests
{
    private readonly FakeEntityService _entities = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly LogService _log = new();
    private readonly FakeSession _session;
    private readonly ExplorerViewModel _explorer;

    public EntityPanelViewModelTests()
    {
        _entities.Queues.Add(FakeEntityService.Queue("orders", active: 5, deadLetter: 2));
        _entities.Definitions["orders"] = new QueueDefinition { Name = "orders", MaxDeliveryCount = 7 };
        _entities.Topics.Add(FakeEntityService.Topic("events"));
        _entities.Definitions["events"] = new TopicDefinition { Name = "events" };
        _session = new FakeSession(new ConnectionProfile { Name = "x" }, "contoso.servicebus.windows.net", _entities);
        _session.FakeMessages.Seed("orders", 5);
        _session.FakeMessages.Seed("orders/$DeadLetterQueue", 2, i => new MessageInfo { DeadLetterReason = "Bad" });
        _explorer = new ExplorerViewModel(_session, _dialogs, log: _log);
    }

    private async Task<EntityPanelViewModel> SelectAsync(string name)
    {
        await _explorer.RefreshCommand.ExecuteAsync(null);
        _explorer.SelectedNode = _explorer.Roots.SelectMany(r => r.Children).First(n => n.Name == name);
        var panel = _explorer.Details!;
        await panel.EditorLoad;
        return panel;
    }

    [Fact]
    public async Task Selecting_a_queue_shows_its_settings_information_and_title()
    {
        var panel = await SelectAsync("orders");

        Assert.Equal("View Queue: orders", _explorer.DetailsTitle);
        Assert.Equal(7, panel.Editor!.MaxDeliveryCount);
        Assert.Equal("Queue Information", panel.InformationTitle);
        Assert.True(panel.SupportsMessages);
        Assert.Equal("Disable", panel.StatusButtonLabel);
        Assert.False(panel.ShowMessagesTab);
    }

    [Fact]
    public async Task Messages_button_asks_how_to_retrieve_then_opens_the_tab()
    {
        var panel = await SelectAsync("orders");
        _dialogs.OnReceiveOptions = options =>
        {
            Assert.Equal("Retrieve messages from orders", options.Title);
            Assert.Equal("All (5)", options.AllLabel);
            options.Count = 3;
            return true;
        };

        await panel.MessagesCommand.ExecuteAsync(null);

        Assert.True(panel.ShowMessagesTab);
        Assert.Equal(EntityPanelViewModel.MessagesTab, panel.SelectedTab);
        Assert.Equal(3, panel.Messages!.Messages.Count);
        Assert.Contains(_log.Entries, e => e.Message.StartsWith("Orders: Peeked 3 messages"));
    }

    [Fact]
    public async Task Cancelling_the_retrieve_dialog_changes_nothing()
    {
        var panel = await SelectAsync("orders");
        _dialogs.OnReceiveOptions = _ => false;

        await panel.MessagesCommand.ExecuteAsync(null);

        Assert.False(panel.ShowMessagesTab);
        Assert.Empty(panel.Messages!.Messages);
    }

    [Fact]
    public async Task Dead_letter_button_opens_the_deadletter_tab_on_the_dead_letter_queue()
    {
        var panel = await SelectAsync("orders");
        _dialogs.OnReceiveOptions = options =>
        {
            options.IsAll = true;
            return true;
        };

        await panel.DeadletterCommand.ExecuteAsync(null);

        Assert.True(panel.ShowDeadletterTab);
        Assert.Equal(EntityPanelViewModel.DeadletterTab, panel.SelectedTab);
        Assert.Equal(SubQueueKind.DeadLetter, panel.DeadLetters!.Source.SubQueue);
        Assert.False(panel.DeadLetters.ShowSubQueues);
        Assert.All(panel.DeadLetters.Messages, m => Assert.Equal("Bad", m.DeadLetterReason));
    }

    [Fact]
    public async Task Receive_and_lock_from_the_dialog_then_close_tabs_releases_the_locks()
    {
        var panel = await SelectAsync("orders");
        _dialogs.OnReceiveOptions = options =>
        {
            options.Mode = RetrieveMode.ReceiveLocked;
            return true;
        };
        await panel.MessagesCommand.ExecuteAsync(null);
        Assert.True(panel.Messages!.HasLockedMessages);

        await panel.CloseTabsCommand.ExecuteAsync(null);

        Assert.True(_session.FakeMessages.Batches.Single().Disposed);
        Assert.False(panel.ShowMessagesTab);
        Assert.Equal(EntityPanelViewModel.DescriptionTab, panel.SelectedTab);
    }

    [Fact]
    public async Task Disable_button_toggles_the_status()
    {
        var panel = await SelectAsync("orders");

        await panel.ToggleStatusCommand.ExecuteAsync(null);

        Assert.Equal("status orders Disabled", _entities.Calls.Single());
        Assert.Equal("Enable", _explorer.Details!.StatusButtonLabel);
    }

    [Fact]
    public async Task Update_saves_the_description_and_logs_it()
    {
        var panel = await SelectAsync("orders");
        panel.Editor!.MaxDeliveryCount = 9;

        await panel.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(9, ((QueueDefinition)_entities.Definitions["orders"]).MaxDeliveryCount);
        Assert.Contains(_log.Entries, e => e.Message == "The queue orders has been successfully updated.");
        Assert.Equal("orders", _explorer.SelectedEntity?.Name);
    }

    [Fact]
    public async Task Topics_send_from_the_panel_but_have_no_message_tabs()
    {
        var panel = await SelectAsync("events");
        _dialogs.OnSend = async sender =>
        {
            Assert.Equal("events", sender.Target);
            sender.Body = "hi";
            await sender.SendCommand.ExecuteAsync(null);
            return true;
        };

        await panel.SendCommand.ExecuteAsync(null);

        Assert.False(panel.SupportsMessages);
        Assert.Null(panel.Messages);
        Assert.Equal("events", _session.FakeMessages.Sent.Single().Entity);
        Assert.Equal("Topic Information", panel.InformationTitle);
    }

    [Fact]
    public async Task Panels_are_kept_while_browsing_the_tree()
    {
        var panel = await SelectAsync("orders");
        await panel.MessagesCommand.ExecuteAsync(null);

        _explorer.SelectedNode = _explorer.Roots[1].Children[0];
        _explorer.SelectedNode = _explorer.Roots[0].Children[0];

        Assert.Same(panel, _explorer.Details);
        Assert.True(_explorer.Details!.ShowMessagesTab);
    }

    [Fact]
    public async Task Creating_replaces_the_panel_until_cancelled_or_another_node_is_picked()
    {
        await SelectAsync("orders");

        _explorer.NewQueueCommand.Execute(null);
        Assert.True(_explorer.IsCreating);
        Assert.False(_explorer.ShowsDetails);

        _explorer.CancelCreateCommand.Execute(null);
        Assert.True(_explorer.ShowsDetails);

        _explorer.NewTopicCommand.Execute(null);
        _explorer.SelectedNode = _explorer.Roots[1].Children[0];
        Assert.False(_explorer.IsCreating);
    }

    [Theory]
    [InlineData(false, 25, null, 25)]
    [InlineData(true, 25, 120L, 120)]
    [InlineData(true, 25, null, ReceiveOptionsViewModel.AllLimit)]
    [InlineData(true, 25, 50_000L, ReceiveOptionsViewModel.AllLimit)]
    public void Retrieve_options_resolve_how_many_messages(bool all, int top, long? known, int expected)
    {
        var options = new ReceiveOptionsViewModel("x", canReceive: true, known) { IsAll = all, Count = top };

        Assert.Equal(expected, options.EffectiveCount);
    }

    [Fact]
    public void Duration_boxes_follow_the_text_and_write_it_back()
    {
        var editor = EntityEditorViewModel.NewQueue(_entities);
        Assert.Equal((10675199, 2, 48, 5, 477), (editor.DefaultMessageTimeToLiveParts.Days, editor.DefaultMessageTimeToLiveParts.Hours,
            editor.DefaultMessageTimeToLiveParts.Minutes, editor.DefaultMessageTimeToLiveParts.Seconds, editor.DefaultMessageTimeToLiveParts.Milliseconds));

        editor.LockDurationParts.Minutes = 0;
        editor.LockDurationParts.Seconds = 45;
        Assert.Equal("45s", editor.LockDuration);

        editor.LockDuration = "2m";
        Assert.Equal((2, 0), (editor.LockDurationParts.Minutes, editor.LockDurationParts.Seconds));

        editor.LockDurationParts.Minutes = 0;
        Assert.False(editor.IsValid);
    }
}
