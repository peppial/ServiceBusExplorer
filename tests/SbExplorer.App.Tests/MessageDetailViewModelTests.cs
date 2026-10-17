using SbExplorer.App.ViewModels;
using SbExplorer.Core.Messaging;
using Xunit;

namespace SbExplorer.App.Tests;

public class MessageDetailViewModelTests
{
    private readonly FakeDialogs _dialogs = new();

    [Fact]
    public void Shows_formatted_body_with_summary_and_hex_toggle()
    {
        var vm = new MessageDetailViewModel(new MessageInfo { Body = """{"a":1}"""u8.ToArray(), ContentType = "application/json" }, _dialogs);

        Assert.Equal("JSON · 7 B", vm.BodySummary);
        Assert.StartsWith("{\n", vm.BodyText.ReplaceLineEndings("\n"));
        Assert.True(vm.CanToggleHex);

        vm.ShowHex = true;
        Assert.StartsWith("00000000  7B 22 61", vm.BodyText);
    }

    [Fact]
    public void Binary_bodies_are_hex_without_toggle()
    {
        var vm = new MessageDetailViewModel(new MessageInfo { Body = new byte[] { 0, 1, 2 }, BodyType = "Value" }, _dialogs);

        Assert.False(vm.CanToggleHex);
        Assert.Equal("Binary · 3 B · AMQP Value body", vm.BodySummary);
    }

    [Fact]
    public void Lists_only_relevant_system_properties_and_sorted_application_properties()
    {
        var vm = new MessageDetailViewModel(new MessageInfo
        {
            SequenceNumber = 7,
            DeadLetterReason = "ValidationFailed",
            ApplicationProperties = new Dictionary<string, object?> { ["z"] = 1, ["a"] = "x" },
        }, _dialogs);

        Assert.Contains(vm.SystemProperties, p => p is { Name: "Dead-letter reason", Value: "ValidationFailed" });
        Assert.DoesNotContain(vm.SystemProperties, p => p.Name == "Session ID");
        Assert.Equal(["a", "z"], vm.ApplicationProperties.Select(p => p.Name));
        Assert.Equal("Int32", vm.ApplicationProperties[1].Type);
    }

    [Fact]
    public async Task Copy_and_save_body()
    {
        var vm = new MessageDetailViewModel(new MessageInfo { MessageId = "order/1", Body = "hi"u8.ToArray() }, _dialogs);

        await vm.CopyBodyCommand.ExecuteAsync(null);
        await vm.SaveBodyCommand.ExecuteAsync(null);

        Assert.Equal("hi", _dialogs.CopiedText);
        var saved = _dialogs.SavedFiles.Single();
        Assert.Equal("order_1.txt", saved.Name);
        Assert.Equal("hi"u8.ToArray(), saved.Content);
    }
}
