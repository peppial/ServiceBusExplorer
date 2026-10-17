using System.Text;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

public class SendMessageViewModelTests
{
    private readonly FakeMessageService _service = new();

    [Fact]
    public void Session_entities_require_a_session_id()
    {
        var vm = new SendMessageViewModel(_service, "orders-sessions", requiresSession: true);
        Assert.False(vm.SendCommand.CanExecute(null));

        vm.SessionId = "customer-a";
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("00:10:00", true)]
    [InlineData("1.00:00:00", true)]
    [InlineData("ten minutes", false)]
    [InlineData("00:00:00", false)]
    [InlineData("", true)]
    public void Validates_time_to_live(string ttl, bool valid)
    {
        var vm = new SendMessageViewModel(_service, "orders", false) { TimeToLive = ttl };

        Assert.Equal(valid, vm.IsValid);
    }

    [Fact]
    public void Validates_application_properties_as_they_are_edited()
    {
        var vm = new SendMessageViewModel(_service, "orders", false);
        vm.AddPropertyCommand.Execute(null);
        Assert.Equal("Every application property needs a name.", vm.ValidationError);

        var row = vm.ApplicationProperties[0];
        row.Name = "attempt";
        row.Type = PropertyValueType.Int32;
        row.Value = "two";
        Assert.Equal("attempt: 'two' is not a valid Int32.", vm.ValidationError);
        Assert.False(vm.SendCommand.CanExecute(null));

        row.Value = "2";
        Assert.True(vm.SendCommand.CanExecute(null));

        vm.AddPropertyCommand.Execute(null);
        vm.ApplicationProperties[1].Name = "attempt";
        Assert.Equal("Application property 'attempt' appears twice.", vm.ValidationError);

        vm.RemovePropertyCommand.Execute(vm.ApplicationProperties[1]);
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void Builds_copies_with_numbered_message_ids_and_typed_properties()
    {
        var vm = new SendMessageViewModel(_service, "orders", false)
        {
            Body = "{}",
            MessageId = "batch",
            Count = 3,
            TimeToLive = "00:05:00",
            Subject = "  ",
        };
        vm.ApplicationProperties.Add(new ApplicationPropertyRowViewModel { Name = "n", Value = "5", Type = PropertyValueType.Int64 });

        var messages = vm.BuildMessages();

        Assert.Equal(["batch-1", "batch-2", "batch-3"], messages.Select(m => m.MessageId));
        Assert.All(messages, m =>
        {
            Assert.Equal(5L, m.ApplicationProperties["n"]);
            Assert.Equal(TimeSpan.FromMinutes(5), m.TimeToLive);
            Assert.Null(m.Subject);
            Assert.Equal("application/json", m.ContentType);
        });
    }

    [Fact]
    public void Warns_about_invalid_json_without_blocking_send()
    {
        var vm = new SendMessageViewModel(_service, "orders", false) { Body = "{oops" };

        Assert.Equal("The body isn't valid JSON.", vm.BodyWarning);
        Assert.True(vm.SendCommand.CanExecute(null));

        vm.ContentType = "text/plain";
        Assert.Null(vm.BodyWarning);
    }

    [Fact]
    public void Format_pretty_prints_the_body()
    {
        var vm = new SendMessageViewModel(_service, "orders", false) { Body = """{"a":1}""" };

        vm.FormatBodyCommand.Execute(null);

        Assert.Equal("{\n  \"a\": 1\n}", vm.Body.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Binary_template_is_resent_byte_for_byte()
    {
        byte[] raw = [0x00, 0xFF, 0x10];
        var vm = new SendMessageViewModel(_service, "payments", false, new MessageInfo { Body = raw, ContentType = "application/octet-stream" });

        Assert.True(vm.IsBinaryBody);
        Assert.Equal(raw, vm.BuildMessages().Single().Body.ToArray());
    }

    [Fact]
    public async Task Send_failure_keeps_the_dialog_open_with_the_error()
    {
        _service.Failure = new InvalidOperationException("Quota exceeded");
        var vm = new SendMessageViewModel(_service, "orders", false) { Body = "x" };
        var closed = false;
        vm.Sent += (_, _) => closed = true;

        await vm.SendCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Equal("Quota exceeded", vm.SendError);
    }

    [Fact]
    public async Task Successful_send_raises_sent_with_the_count()
    {
        var vm = new SendMessageViewModel(_service, "orders", false) { Body = "hi", Count = 2 };
        var closed = false;
        vm.Sent += (_, _) => closed = true;

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.Equal(2, vm.SentCount);
        Assert.All(_service.Sent, s => Assert.Equal("hi", Encoding.UTF8.GetString(s.Message.Body.Span)));
    }

    [Fact]
    public void Scheduling_validates_and_sets_the_enqueue_time()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
        var vm = new SendMessageViewModel(_service, "orders", false, clock: clock) { Schedule = true, ScheduleAt = "soon" };
        Assert.False(vm.IsValid);

        vm.ScheduleAt = "+1h";

        Assert.True(vm.IsValid);
        Assert.Equal(clock.GetUtcNow().AddHours(1), vm.BuildMessages().Single().ScheduledEnqueueTime);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
