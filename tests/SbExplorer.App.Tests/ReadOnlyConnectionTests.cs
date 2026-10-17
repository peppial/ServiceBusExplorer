using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

public class ReadOnlyConnectionTests
{
    private readonly FakeEntityService _entities = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeSession _session;
    private readonly ExplorerViewModel _explorer;

    public ReadOnlyConnectionTests()
    {
        _entities.Queues.Add(FakeEntityService.Queue("orders", active: 2) with { RequiresSession = true });
        _entities.Definitions["orders"] = new QueueDefinition { Name = "orders", RequiresSession = true };
        _entities.Topics.Add(FakeEntityService.Topic("events"));
        _entities.Definitions["events"] = new TopicDefinition { Name = "events" };
        _session = new FakeSession(new ConnectionProfile { Name = "prod", ReadOnly = true }, "prod.servicebus.windows.net", _entities);
        _session.FakeMessages.Seed("orders", 2, i => new MessageInfo { SessionId = "s1" });
        _session.FakeMessages.Seed("orders/$DeadLetterQueue", 1);
        _explorer = new ExplorerViewModel(_session, _dialogs);
    }

    private async Task<EntityPanelViewModel> SelectOrdersAsync()
    {
        await _explorer.RefreshCommand.ExecuteAsync(null);
        _explorer.SelectedNode = _explorer.Roots[0].Children[0];
        await _explorer.Details!.EditorLoad;
        return _explorer.Details;
    }

    [Fact]
    public async Task Entity_changes_are_disabled_but_browsing_and_export_work()
    {
        var panel = await SelectOrdersAsync();

        Assert.True(_explorer.IsReadOnly);
        Assert.False(_explorer.CanModify);
        Assert.False(_explorer.NewQueueCommand.CanExecute(null));
        Assert.False(_explorer.ImportCommand.CanExecute(null));
        Assert.False(_explorer.DeleteCommand.CanExecute(null));
        Assert.False(_explorer.SetStatusCommand.CanExecute(EntityStatusValue.Disabled));
        Assert.True(_explorer.ExportCommand.CanExecute(null));
        Assert.True(_explorer.RefreshCommand.CanExecute(null));

        Assert.False(panel.UpdateCommand.CanExecute(null));
        Assert.False(panel.ToggleStatusCommand.CanExecute(null));
        Assert.False(panel.DeleteCommand.CanExecute(null));
        Assert.False(panel.PurgeCommand.CanExecute(null));
        Assert.False(panel.SendCommand.CanExecute(null));
        Assert.NotNull(panel.ReadOnlyNotice);
        Assert.True(panel.MessagesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Messages_can_only_be_peeked()
    {
        var panel = await SelectOrdersAsync();
        _dialogs.OnReceiveOptions = options =>
        {
            Assert.False(options.CanReceive);
            return true;
        };
        await panel.MessagesCommand.ExecuteAsync(null);

        var messages = panel.Messages!;
        Assert.Equal(2, messages.Messages.Count);
        Assert.True(messages.PeekCommand.CanExecute(null));
        Assert.False(messages.ReceiveLockedCommand.CanExecute(null));
        Assert.False(messages.ReceiveAndDeleteCommand.CanExecute(null));
        Assert.False(messages.SendCommand.CanExecute(null));
        Assert.False(messages.ResendCommand.CanExecute(null));
        Assert.False(messages.PurgeCommand.CanExecute(null));
        Assert.StartsWith("Read-only connection", messages.Notice);

        var deadLetters = panel.DeadLetters!;
        await deadLetters.PeekCommand.ExecuteAsync(null);
        deadLetters.SelectedRows = deadLetters.Messages.ToList();
        Assert.False(deadLetters.ResubmitCommand.CanExecute(null));
        Assert.False(deadLetters.EditAndResubmitCommand.CanExecute(null));
        Assert.False(deadLetters.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sessions_can_be_found_and_state_read_but_not_changed()
    {
        var panel = await SelectOrdersAsync();
        var sessions = panel.Sessions!;

        await sessions.FindSessionsCommand.ExecuteAsync(null);

        Assert.Equal("s1", sessions.SelectedSession?.SessionId);
        Assert.True(sessions.LoadStateCommand.CanExecute(null));
        Assert.False(sessions.SaveStateCommand.CanExecute(null));
        Assert.False(sessions.ClearStateCommand.CanExecute(null));
        Assert.False(sessions.Messages!.ReceiveLockedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Connect_dialog_saves_the_read_only_choice_and_the_window_shows_it()
    {
        var secrets = new FakeSecretStore();
        var store = new FakeConnectionStore(secrets);
        var log = new LogService();
        var dialogs = new FakeDialogs
        {
            OnConnectDialog = async dialog =>
            {
                dialog.Editor.Name = "Production";
                dialog.Editor.ConnectionString = "Endpoint=sb://prod.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v";
                dialog.Editor.ReadOnly = true;
                await dialog.SaveCommand.ExecuteAsync(null);
                dialog.OkCommand.Execute(null);
                return true;
            },
        };
        var vm = new MainWindowViewModel(store, secrets, new FakeConnector(), dialogs, log);

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(store.Profiles.Single().ReadOnly);
        Assert.Equal("prod.servicebus.windows.net (read-only) - Service Bus Explorer", vm.Title);
        Assert.Contains(log.Entries, e => e.Message.StartsWith("Read-only connection"));
        Assert.True(vm.Explorer!.IsReadOnly);
    }
}
