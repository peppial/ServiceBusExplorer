using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbExplorer.Core.Connections;

/// <summary>Stores profiles as JSON in a single file and delegates secrets to an <see cref="ISecretStore"/>.</summary>
public sealed class JsonConnectionStore(string filePath, ISecretStore secrets) : IConnectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await ReadAsync(cancellationToken)).ToList();
            var index = profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0)
            {
                profiles[index] = profile;
            }
            else
            {
                profiles.Add(profile);
            }

            if (secret is not null)
            {
                await secrets.SetAsync(profile.SecretKey, secret, cancellationToken);
            }
            else if (profile.Auth == AuthKind.Entra)
            {
                await secrets.DeleteAsync(profile.SecretKey, cancellationToken);
            }

            await WriteAsync(profiles, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await ReadAsync(cancellationToken)).ToList();
            var profile = profiles.Find(p => p.Id == id);
            if (profile is null)
            {
                return;
            }

            profiles.Remove(profile);
            await WriteAsync(profiles, cancellationToken);
            await secrets.DeleteAsync(profile.SecretKey, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<string?> GetSecretAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        secrets.GetAsync(profile.SecretKey, cancellationToken);

    private async Task<IReadOnlyList<ConnectionProfile>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(filePath);
        var document = await JsonSerializer.DeserializeAsync<ConnectionsDocument>(stream, SerializerOptions, cancellationToken);
        return document?.Connections ?? [];
    }

    private async Task WriteAsync(List<ConnectionProfile> profiles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

        // Write to a temp file and swap so a crash mid-write never corrupts the saved connections.
        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, new ConnectionsDocument(profiles), SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, filePath, overwrite: true);
    }

    private sealed record ConnectionsDocument(List<ConnectionProfile> Connections)
    {
        public int Version { get; init; } = 1;
    }
}
