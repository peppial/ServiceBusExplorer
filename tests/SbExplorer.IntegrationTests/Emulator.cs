using System.Net.Sockets;
using Azure.Messaging.ServiceBus.Administration;
using SbExplorer.Core.Connections;

namespace SbExplorer.IntegrationTests;

internal static class Emulator
{
    public const string ConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    /// <summary>CI sets this so a missing emulator fails the run instead of silently skipping every test.</summary>
    private static readonly bool Required = Environment.GetEnvironmentVariable("SBX_REQUIRE_EMULATOR") == "1";

    private static readonly Lazy<Task<bool>> Ready = new(WaitUntilReadyAsync);

    public static void SkipIfNotRunning()
    {
        if (Ready.Value.GetAwaiter().GetResult())
        {
            return;
        }

        const string message = "Service Bus emulator is not running (docker compose -f docker/emulator.yml up -d).";
        if (Required)
        {
            throw new InvalidOperationException(message);
        }

        Assert.Skip(message);
    }

    // The emulator accepts connections well before it serves requests ("Service is warming up"), so wait for a
    // management call to succeed once per test run.
    private static async Task<bool> WaitUntilReadyAsync()
    {
        if (!await IsListeningAsync())
        {
            return false;
        }

        var admin = new ServiceBusAdministrationClient(ConnectionStringInfo.ForAdministration(ConnectionString));
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (true)
        {
            try
            {
                await foreach (var _ in admin.GetQueuesAsync().AsPages(pageSizeHint: 1))
                {
                    break;
                }

                return true;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private static async Task<bool> IsListeningAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await client.ConnectAsync("localhost", 5672, timeout.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
