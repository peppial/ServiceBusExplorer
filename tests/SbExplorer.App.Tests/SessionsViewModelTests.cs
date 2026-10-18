using System.Text;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Management;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

public class SessionsViewModelTests
{
    private readonly FakeMessageService _service = new();
    private readonly FakeDialogs _dialogs = new();

    private static readonly EntityInfo Queue = new() { Kind = EntityKind.Queue, Name = "orders-sessions", Path = "orders-sessions", RequiresSession = true };

    private SessionsViewModel Create() => new(_service, _dialogs, Queue);

    private void Seed()
    {
        var sessions = new[] { "b", "a", "b", "c", "b" };
        _service.Seed("orders-sessions", sessions.Length, i => new MessageInfo
        {
            SessionId = sessions[i],
            EnqueuedTime = new DateTimeOffset(2026, 10, 17, 10, i, 0, TimeSpan.Zero),
            Body = Encoding.UTF8.GetBytes($"{sessions[i]}-{i}"),
        });
    }

    [Fact]
    public async Task Find_sessions_groups_peeked_messages_by_session_id()
    {
        Seed();
        var vm = Create();

        await vm.FindSessionsCommand.ExecuteAsync(null);

        Assert.Equal(["a", "b", "c"], vm.Sessions.Select(s => s.SessionId));
        Assert.Equal("3", vm.Sessions[1].MessageCount);
        Assert.Same(vm.Sessions[0], vm.SelectedSession);
        Assert.StartsWith("Found 3 sessions in 5 peeked messages.", vm.Status);
    }

    [Fact]
    public async Task Find_sessions_says_when_it_hit_the_scan_limit()
    {
        Seed();
        var vm = Create();
        vm.ScanLimit = 2;

        await vm.FindSessionsCommand.ExecuteAsync(null);

        Assert.Contains("Stopped at 2 messages", vm.Status);
    }

    [Fact]
    public async Task Selecting_a_session_peeks_its_messages_and_loads_state_only_on_request()
    {
        Seed();
        _service.SessionStates["b"] = """{"step":2}"""u8.ToArray();
        var vm = Create();
        await vm.FindSessionsCommand.ExecuteAsync(null);

        vm.SelectedSession = vm.Sessions.Single(s => s.SessionId == "b");
        await Task.Yield();

        // Reading state locks the session, so selecting it alone doesn't.
        Assert.Equal(string.Empty, vm.StateText);
        Assert.StartsWith("Not loaded", vm.StateSummary);

        await vm.LoadStateCommand.ExecuteAsync(null);
        Assert.Equal("{\n  \"step\": 2\n}", vm.StateText.ReplaceLineEndings("\n"));
        Assert.Equal("JSON · 10 B", vm.StateSummary);
        var messages = vm.Messages!;
        Assert.Equal("b", messages.SessionId);
        Assert.All(messages.Messages, m => Assert.Equal("b", m.SessionId));
        Assert.Equal(3, messages.Messages.Count);
    }

    [Fact]
    public async Task Open_by_id_adds_a_session_the_scan_did_not_find()
    {
        var vm = Create();
        _dialogs.PromptResult = " idle-session ";

        await vm.OpenSessionCommand.ExecuteAsync(null);

        Assert.Equal("idle-session", vm.SelectedSession?.SessionId);
        Assert.Equal("—", vm.SelectedSession?.MessageCount);

        await vm.FindSessionsCommand.ExecuteAsync(null);
        Assert.Contains(vm.Sessions, s => s.SessionId == "idle-session");
    }

    [Fact]
    public async Task Save_and_clear_state()
    {
        var vm = Create();
        _dialogs.PromptResult = "s1";
        await vm.OpenSessionCommand.ExecuteAsync(null);

        vm.StateText = "progress=3";
        await vm.SaveStateCommand.ExecuteAsync(null);
        Assert.Equal("progress=3", Encoding.UTF8.GetString(_service.SessionStates["s1"]));

        await vm.ClearStateCommand.ExecuteAsync(null);
        Assert.True(_service.SessionStates.ContainsKey("s1"));

        _dialogs.ConfirmResult = true;
        await vm.ClearStateCommand.ExecuteAsync(null);
        Assert.False(_service.SessionStates.ContainsKey("s1"));
        Assert.Equal("No state", vm.StateSummary);

        await vm.LoadStateCommand.ExecuteAsync(null);
        Assert.Equal("Session 's1' has no state.", vm.Status);
    }

    [Fact]
    public async Task Binary_state_is_shown_as_hex_and_not_saved_as_text()
    {
        _service.SessionStates["s1"] = [0x00, 0xFF];
        var vm = Create();
        _dialogs.PromptResult = "s1";

        await vm.OpenSessionCommand.ExecuteAsync(null);
        await vm.LoadStateCommand.ExecuteAsync(null);

        Assert.True(vm.StateIsBinary);
        Assert.False(vm.SaveStateCommand.CanExecute(null));
        Assert.StartsWith("00000000  00 FF", vm.StateText);
    }

    [Fact]
    public async Task Session_messages_can_be_received_and_sends_default_to_the_session()
    {
        Seed();
        var vm = Create();
        _dialogs.PromptResult = "b";
        await vm.OpenSessionCommand.ExecuteAsync(null);
        var messages = vm.Messages!;

        Assert.True(messages.CanReceive);
        Assert.False(messages.ShowSubQueues);
        Assert.Null(messages.Notice);

        await messages.ReceiveLockedCommand.ExecuteAsync(null);
        Assert.Equal(3, messages.LockedCount);
        await messages.ReleaseCommand.ExecuteAsync(null);

        _dialogs.OnSend = async sender =>
        {
            Assert.Equal("b", sender.SessionId);
            sender.Body = "next step";
            await sender.SendCommand.ExecuteAsync(null);
            return true;
        };
        await messages.SendCommand.ExecuteAsync(null);
        Assert.Equal("b", _service.Sent.Single().Message.SessionId);
    }

    [Fact]
    public async Task Purging_a_session_entity_drains_every_session()
    {
        Seed();
        var messages = new MessagesViewModel(_service, _dialogs, Queue);
        _dialogs.PromptResult = Queue.Name;

        Assert.False(messages.ReceiveLockedCommand.CanExecute(null));
        Assert.True(messages.PurgeCommand.CanExecute(null));
        await messages.PurgeCommand.ExecuteAsync(null);

        Assert.Equal(1, _service.PurgeAllSessionsCalls);
        Assert.Empty(_service.Messages("orders-sessions"));
        Assert.Equal("Purged 5 messages from every session of orders-sessions.", messages.Status);
    }

    [Fact]
    public async Task Explorer_shows_a_sessions_tab_only_for_session_entities()
    {
        var entities = new FakeEntityService();
        entities.Queues.AddRange([Queue, FakeEntityService.Queue("plain")]);
        var explorer = new ExplorerViewModel(new FakeSession(new() { Name = "x" }, "x", entities), _dialogs);
        await explorer.RefreshCommand.ExecuteAsync(null);

        explorer.SelectedNode = explorer.Roots[0].Children.Single(c => c.Name == "orders-sessions");
        Assert.True(explorer.HasSessions);

        explorer.SelectedNode = explorer.Roots[0].Children.Single(c => c.Name == "plain");
        Assert.False(explorer.HasSessions);
    }
}
