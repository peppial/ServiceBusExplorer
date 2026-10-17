using SbExplorer.App.Services;
using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;

namespace SbExplorer.App.Tests;

internal sealed class FakeSecretStore(bool isSecure = true) : ISecretStore
{
    public Dictionary<string, string> Secrets { get; } = [];
    public bool IsSecure => isSecure;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Secrets.GetValueOrDefault(key));

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        Secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Secrets.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class FakeConnectionStore(ISecretStore secrets) : IConnectionStore
{
    public List<ConnectionProfile> Profiles { get; } = [];

    public Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConnectionProfile>>(Profiles.ToList());

    public async Task SaveAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default)
    {
        Profiles.RemoveAll(p => p.Id == profile.Id);
        Profiles.Add(profile);
        if (secret is not null)
        {
            await secrets.SetAsync(profile.SecretKey, secret, cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Profiles.RemoveAll(p => p.Id == id);
        await secrets.DeleteAsync($"connection:{id:N}", cancellationToken);
    }

    public Task<string?> GetSecretAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
        secrets.GetAsync(profile.SecretKey, cancellationToken);
}

internal sealed class FakeConnector : INamespaceConnector
{
    public Exception? Failure { get; set; }
    public List<FakeSession> Sessions { get; } = [];

    public Task<INamespaceSession> ConnectAsync(ConnectionProfile profile, string? secret, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<INamespaceSession>(Failure);
        }

        var session = new FakeSession(profile, profile.FullyQualifiedNamespace ?? "fake.servicebus.windows.net");
        Sessions.Add(session);
        return Task.FromResult<INamespaceSession>(session);
    }
}

internal sealed class FakeSession(ConnectionProfile profile, string host) : INamespaceSession
{
    public ConnectionProfile Profile { get; } = profile;
    public string FullyQualifiedNamespace { get; } = host;
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    /// <summary>Fills in the editor and returns whether to "save".</summary>
    public Func<ConnectionEditorViewModel, bool> OnEditor { get; set; } = _ => false;
    public bool ConfirmResult { get; set; }
    public int ConfirmCount { get; private set; }

    public Task<bool> ShowConnectionEditorAsync(ConnectionEditorViewModel editor) => Task.FromResult(OnEditor(editor));

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        ConfirmCount++;
        return Task.FromResult(ConfirmResult);
    }
}
