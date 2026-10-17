#:package Azure.Messaging.ServiceBus

// Clears the local emulator, then fills it with sample messages in every state the explorer shows.
//   dotnet run docker/seed.cs
using System.Globalization;
using Azure.Messaging.ServiceBus;

const string connectionString =
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

await using var client = new ServiceBusClient(connectionString);

foreach (var queue in new[] { "orders", "payments", "audit" })
{
    await ClearAsync(queue, null);
}

foreach (var subscription in new[] { "all-events", "high-priority" })
{
    await ClearAsync("events", subscription);
}

await ClearSessionsAsync("orders-sessions");

// orders: 20 JSON orders, 3 dead-lettered, 2 scheduled 30 minutes ahead, 1 deferred.
await using (var sender = client.CreateSender("orders"))
{
    var batch = Enumerable.Range(1, 20).Select(i => new ServiceBusMessage(
        string.Create(CultureInfo.InvariantCulture,
        $$"""{"orderId":{{1000 + i}},"customer":"customer-{{i % 5}}","total":{{i * 12.5:0.00}},"items":[{"sku":"SKU-{{i}}","qty":{{i % 3 + 1}}}]}"""))
    {
        MessageId = $"order-{1000 + i}",
        ContentType = "application/json",
        Subject = "order-created",
        CorrelationId = $"cart-{i % 7}",
        ApplicationProperties = { ["region"] = i % 2 == 0 ? "eu" : "us", ["priority"] = i % 4 == 0 ? "high" : "normal" },
    });
    await sender.SendMessagesAsync(batch);

    // The emulator caps TTL at one hour, so schedule well inside it.
    var later = DateTimeOffset.UtcNow.AddMinutes(30);
    await sender.ScheduleMessagesAsync(
        [
            new ServiceBusMessage("""{"orderId":2001,"note":"scheduled"}""") { MessageId = "order-2001", ContentType = "application/json", Subject = "order-reminder" },
            new ServiceBusMessage("""{"orderId":2002,"note":"scheduled"}""") { MessageId = "order-2002", ContentType = "application/json", Subject = "order-reminder" },
        ],
        later);
}

await using (var receiver = client.CreateReceiver("orders"))
{
    var received = await receiver.ReceiveMessagesAsync(4, TimeSpan.FromSeconds(5));
    foreach (var message in received.Take(3))
    {
        await receiver.DeadLetterMessageAsync(message, "ValidationFailed", $"Order {message.MessageId} has an invalid shipping address.");
    }

    if (received.Count > 3)
    {
        await receiver.DeferMessageAsync(received[3]);
        Console.WriteLine($"orders: deferred sequence number {received[3].SequenceNumber}");
    }
}

// orders-sessions: three customer sessions, one with session state.
await using (var sender = client.CreateSender("orders-sessions"))
{
    foreach (var session in new[] { "customer-a", "customer-b", "customer-c" })
    {
        await sender.SendMessagesAsync(Enumerable.Range(1, 4).Select(step => new ServiceBusMessage($"{session} step {step}")
        {
            SessionId = session,
            ContentType = "text/plain",
            Subject = "workflow-step",
        }));
    }
}

await using (var sessionReceiver = await client.AcceptSessionAsync("orders-sessions", "customer-a"))
{
    await sessionReceiver.SetSessionStateAsync(BinaryData.FromString("""{"lastStep":2,"status":"in-progress"}"""));
}

// payments: XML, binary and duplicate-detected messages.
await using (var sender = client.CreateSender("payments"))
{
    await sender.SendMessageAsync(new ServiceBusMessage("<payment><id>P-1</id><amount currency=\"EUR\">49.90</amount></payment>")
    {
        MessageId = "payment-1",
        ContentType = "application/xml",
        Subject = "payment-received",
    });
    await sender.SendMessageAsync(new ServiceBusMessage(new BinaryData(new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x7F, 0x80 }))
    {
        MessageId = "payment-2",
        ContentType = "application/octet-stream",
        Subject = "payment-raw",
    });
    // Same MessageId again: duplicate detection drops it, so the queue holds 2 messages.
    await sender.SendMessageAsync(new ServiceBusMessage("duplicate") { MessageId = "payment-1" });
}

// events topic: routed to subscriptions by the SQL and correlation filters.
await using (var sender = client.CreateSender("events"))
{
    await sender.SendMessagesAsync(
    [
        new ServiceBusMessage("user signed up") { Subject = "user-signup", ApplicationProperties = { ["priority"] = "low" } },
        new ServiceBusMessage("disk almost full") { Subject = "alert", ApplicationProperties = { ["priority"] = "high" } },
        new ServiceBusMessage("""{"invoice":"INV-77","amount":120}""") { Subject = "invoice", ContentType = "application/json", ApplicationProperties = { ["priority"] = "normal" } },
        new ServiceBusMessage("payment overdue") { Subject = "invoice", ApplicationProperties = { ["priority"] = "high" } },
    ]);
}

Console.WriteLine("Cleared and seeded: orders (16 active, 3 dead-lettered, 2 scheduled, 1 deferred), orders-sessions (3 sessions),");
Console.WriteLine("        payments (2), events -> all-events (4), high-priority (2), billing -> audit (2).");

// Removes active, dead-lettered, deferred and scheduled messages so re-seeding starts clean.
async Task ClearAsync(string entity, string? subscription)
{
    foreach (var subQueue in new[] { SubQueue.None, SubQueue.DeadLetter })
    {
        var options = new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete, SubQueue = subQueue };
        await using var receiver = subscription is null
            ? client.CreateReceiver(entity, options)
            : client.CreateReceiver(entity, subscription, options);
        while ((await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1))).Count > 0)
        {
        }

        var leftovers = await receiver.PeekMessagesAsync(500);
        var deferred = leftovers.Where(m => m.State == ServiceBusMessageState.Deferred).Select(m => m.SequenceNumber).ToList();
        if (deferred.Count > 0)
        {
            await receiver.ReceiveDeferredMessagesAsync(deferred);
        }

        var scheduled = leftovers.Where(m => m.State == ServiceBusMessageState.Scheduled).Select(m => m.SequenceNumber).ToList();
        if (scheduled.Count > 0)
        {
            await using var sender = client.CreateSender(entity);
            await sender.CancelScheduledMessagesAsync(scheduled);
        }
    }
}

// Drains each session by ID. AcceptNextSession with a timeout is avoided: a cancelled accept can stay pending
// on the broker and swallow the sessions this script is about to send.
async Task ClearSessionsAsync(string queue)
{
    await using var browser = client.CreateReceiver(queue);
    var sessions = (await browser.PeekMessagesAsync(1000)).Select(m => m.SessionId).Distinct().ToList();
    foreach (var sessionId in sessions)
    {
        await using var session = await client.AcceptSessionAsync(
            queue, sessionId, new ServiceBusSessionReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
        while ((await session.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1))).Count > 0)
        {
        }

        await session.SetSessionStateAsync(null);
    }

    // Session state can outlive its messages; clear the seeded one explicitly.
    await using var seeded = await client.AcceptSessionAsync(queue, "customer-a");
    await seeded.SetSessionStateAsync(null);
}
