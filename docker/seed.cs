#:package Azure.Messaging.ServiceBus

// Fills the local emulator with sample messages in every state the explorer shows.
//   dotnet run docker/seed.cs
using System.Text;
using Azure.Messaging.ServiceBus;

const string connectionString =
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

await using var client = new ServiceBusClient(connectionString);

// orders: 20 JSON orders, 3 dead-lettered, 2 scheduled 30 minutes ahead, 1 deferred.
await using (var sender = client.CreateSender("orders"))
{
    var batch = Enumerable.Range(1, 20).Select(i => new ServiceBusMessage(
        $$"""{"orderId":{{1000 + i}},"customer":"customer-{{i % 5}}","total":{{i * 12.5:0.00}},"items":[{"sku":"SKU-{{i}}","qty":{{i % 3 + 1}}}]}""")
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

Console.WriteLine("Seeded: orders (16 active, 3 dead-lettered, 2 scheduled, 1 deferred), orders-sessions (3 sessions),");
Console.WriteLine("        payments (2), events -> all-events (4), high-priority (2), billing -> audit (2).");
