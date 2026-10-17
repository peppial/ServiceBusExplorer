using System.Net.Sockets;

namespace SbExplorer.IntegrationTests;

internal static class Emulator
{
    public const string ConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private static readonly Lazy<bool> Running = new(() =>
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("localhost", 5672).Wait(TimeSpan.FromSeconds(1)) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public static void SkipIfNotRunning() =>
        Assert.SkipUnless(Running.Value, "Service Bus emulator is not running (docker compose -f docker/emulator.yml up -d).");
}
