using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SbExplorer.Core.Connections;

namespace SbExplorer.Infrastructure.Secrets;

/// <summary>Secrets encrypted with DPAPI for the current Windows user, kept in one JSON file.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiSecretStore(string directory) : ISecretStore
{
    private static readonly byte[] Entropy = "ServiceBusExplorer"u8.ToArray();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string SecretsPath => Path.Combine(directory, "secrets.dpapi.json");

    public bool IsSecure => true;

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAsync(cancellationToken);
            if (!entries.TryGetValue(key, out var payload))
            {
                return null;
            }

            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(payload), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAsync(cancellationToken);
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
            entries[key] = Convert.ToBase64String(bytes);
            await WriteAsync(entries, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAsync(cancellationToken);
            if (entries.Remove(key))
            {
                await WriteAsync(entries, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SecretsPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(SecretsPath);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken) ?? [];
    }

    private async Task WriteAsync(Dictionary<string, string> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await using var stream = File.Create(SecretsPath);
        await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: cancellationToken);
    }
}
