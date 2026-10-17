using SbExplorer.Core.Connections;

namespace SbExplorer.Core.Tests;

public class ConnectionStringInfoTests
{
    [Fact]
    public void Parses_sas_connection_string()
    {
        var ok = ConnectionStringInfo.TryParse(
            "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=;EntityPath=orders",
            out var info, out var error);

        Assert.True(ok, error);
        Assert.Equal("contoso.servicebus.windows.net", info!.FullyQualifiedNamespace);
        Assert.Equal("RootManageSharedAccessKey", info.SharedAccessKeyName);
        Assert.Equal("orders", info.EntityPath);
        Assert.False(info.UseDevelopmentEmulator);
    }

    [Fact]
    public void Keeps_equals_signs_inside_key_values()
    {
        ConnectionStringInfo.TryParse(
            "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=a=b==",
            out var info, out _);

        Assert.NotNull(info);
    }

    [Fact]
    public void Detects_emulator()
    {
        var ok = ConnectionStringInfo.TryParse(
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
            out var info, out _);

        Assert.True(ok);
        Assert.True(info!.UseDevelopmentEmulator);
        Assert.Equal("localhost", info.FullyQualifiedNamespace);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("SharedAccessKeyName=k;SharedAccessKey=v", "no Endpoint")]
    [InlineData("Endpoint=not a uri;SharedAccessKeyName=k;SharedAccessKey=v", "not a valid URI")]
    [InlineData("Endpoint=sb://x.servicebus.windows.net/", "SharedAccessKeyName")]
    [InlineData("Endpoint=sb://x.servicebus.windows.net/;garbage", "Malformed")]
    public void Rejects_invalid_strings(string value, string expectedError)
    {
        var ok = ConnectionStringInfo.TryParse(value, out var info, out var error);

        Assert.False(ok);
        Assert.Null(info);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void Points_emulator_administration_at_management_port()
    {
        var admin = ConnectionStringInfo.ForAdministration(
            "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;");

        Assert.StartsWith("Endpoint=sb://localhost:5300/;", admin);
    }

    [Theory]
    [InlineData("Endpoint=sb://localhost:6000;SharedAccessKeyName=k;SharedAccessKey=v;UseDevelopmentEmulator=true")]
    [InlineData("Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v")]
    public void Leaves_other_connection_strings_alone(string connectionString) =>
        Assert.Equal(connectionString, ConnectionStringInfo.ForAdministration(connectionString));
}
