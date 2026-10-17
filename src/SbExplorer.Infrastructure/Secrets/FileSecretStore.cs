using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SbExplorer.Core.Connections;

namespace SbExplorer.Infrastructure.Secrets;

/// <summary>
/// Fallback when no OS keychain is available. Secrets are AES-GCM encrypted with a random key kept in a
/// user-only file next to them, which stops casual reading but not someone with access to the account.
/// </summary>
public sealed class FileSecretStore(string directory) : ISecretStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string SecretsPath => Path.Combine(directory, "secrets.json");
    private string KeyPath => Path.Combine(directory, "secrets.key");

    public bool IsSecure => false;

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAsync(cancellationToken);
            return entries.TryGetValue(key, out var payload) ? Decrypt(payload) : null;
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
            entries[key] = Encrypt(secret);
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
        await using (var stream = File.Create(SecretsPath))
        {
            await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: cancellationToken);
        }

        RestrictToCurrentUser(SecretsPath);
    }

    private string Encrypt(string secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(LoadOrCreateKey(), tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    private string? Decrypt(string payload)
    {
        var bytes = Convert.FromBase64String(payload);
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var plaintext = new byte[bytes.Length - nonceSize - tagSize];

        try
        {
            using var aes = new AesGcm(LoadOrCreateKey(), tagSize);
            aes.Decrypt(bytes.AsSpan(0, nonceSize), bytes.AsSpan(nonceSize + tagSize), bytes.AsSpan(nonceSize, tagSize), plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            // The key file was replaced or deleted; the secret is unrecoverable.
            return null;
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(KeyPath))
        {
            return File.ReadAllBytes(KeyPath);
        }

        Directory.CreateDirectory(directory);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(KeyPath, key);
        RestrictToCurrentUser(KeyPath);
        return key;
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
