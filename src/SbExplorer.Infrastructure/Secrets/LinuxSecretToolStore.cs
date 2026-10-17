using System.Diagnostics;
using System.Runtime.Versioning;
using SbExplorer.Core.Connections;

namespace SbExplorer.Infrastructure.Secrets;

/// <summary>Uses libsecret (GNOME Keyring, KWallet) through the <c>secret-tool</c> CLI.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretToolStore(string serviceName = "ServiceBusExplorer") : ISecretStore
{
    public bool IsSecure => true;

    public static bool IsAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, "secret-tool")));
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var (exitCode, output) = await RunAsync(["lookup", "service", serviceName, "account", key], stdin: null, cancellationToken);
        return exitCode == 0 && output.Length > 0 ? output : null;
    }

    public async Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        // The secret goes through stdin so it never appears in the process list.
        var (exitCode, _) = await RunAsync(
            ["store", $"--label=Service Bus Explorer ({key})", "service", serviceName, "account", key], secret, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"secret-tool store failed with exit code {exitCode}.");
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        RunAsync(["clear", "service", serviceName, "account", key], stdin: null, cancellationToken);

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string[] arguments, string? stdin, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }

        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, output);
    }
}
