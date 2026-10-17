using SbExplorer.App.ViewModels;
using SbExplorer.Core.Connections;
using Xunit;

namespace SbExplorer.App.Tests;

public class ConnectionEditorViewModelTests
{
    private readonly FakeConnector _connector = new();

    [Fact]
    public void New_editor_is_invalid_until_named_and_given_a_valid_connection_string()
    {
        var editor = new ConnectionEditorViewModel(_connector);
        Assert.False(editor.SaveCommand.CanExecute(null));

        editor.Name = "Orders";
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Contains("empty", editor.ValidationError);

        editor.ConnectionString = "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v";
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("contoso", "contoso.servicebus.windows.net")]
    [InlineData("contoso.servicebus.windows.net", "contoso.servicebus.windows.net")]
    [InlineData("https://contoso.servicebus.windows.net:443/", "contoso.servicebus.windows.net")]
    [InlineData("contoso.servicebus.chinacloudapi.cn", "contoso.servicebus.chinacloudapi.cn")]
    public void Entra_profile_normalizes_namespace(string input, string expected)
    {
        var editor = new ConnectionEditorViewModel(_connector) { Name = "x", UseEntra = true, Namespace = input };

        var profile = editor.ToProfile();

        Assert.Equal(AuthKind.Entra, profile.Auth);
        Assert.Equal(expected, profile.FullyQualifiedNamespace);
        Assert.Null(editor.Secret);
    }

    [Fact]
    public void Use_emulator_fills_connection_string_and_name()
    {
        var editor = new ConnectionEditorViewModel(_connector);

        editor.UseEmulatorCommand.Execute(null);

        Assert.True(editor.IsValid);
        Assert.Equal("Local emulator", editor.Name);
        Assert.Contains("UseDevelopmentEmulator=true", editor.Secret);
    }

    [Fact]
    public void Editing_keeps_profile_id_and_loads_existing_values()
    {
        var existing = new ConnectionProfile
        {
            Name = "Billing",
            Auth = AuthKind.Entra,
            FullyQualifiedNamespace = "billing.servicebus.windows.net",
            EntraCredential = EntraCredentialKind.AzureCli,
            TenantId = "tenant",
        };

        var editor = new ConnectionEditorViewModel(_connector, existing);

        Assert.False(editor.IsNew);
        Assert.Equal(existing, editor.ToProfile());
    }

    [Fact]
    public async Task Test_reports_success_and_failure()
    {
        var editor = new ConnectionEditorViewModel(_connector) { Name = "x", UseEntra = true, Namespace = "contoso" };

        await editor.TestCommand.ExecuteAsync(null);
        Assert.True(editor.TestSucceeded);
        Assert.Equal("Connected to contoso.servicebus.windows.net.", editor.TestResult);
        Assert.True(Assert.Single(_connector.Sessions).Disposed);

        _connector.Failure = new InvalidOperationException("boom");
        await editor.TestCommand.ExecuteAsync(null);
        Assert.False(editor.TestSucceeded);
        Assert.Equal("boom", editor.TestResult);
    }
}
