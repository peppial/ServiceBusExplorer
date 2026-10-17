using Azure.Core;
using Azure.Identity;
using SbExplorer.Core.Connections;

namespace SbExplorer.Infrastructure.Connections;

public static class CredentialFactory
{
    public static TokenCredential Create(ConnectionProfile profile)
    {
        var tenantId = string.IsNullOrWhiteSpace(profile.TenantId) ? null : profile.TenantId.Trim();

        return profile.EntraCredential switch
        {
            EntraCredentialKind.InteractiveBrowser => new InteractiveBrowserCredential(
                new InteractiveBrowserCredentialOptions { TenantId = tenantId }),
            EntraCredentialKind.DeviceCode => new DeviceCodeCredential(
                new DeviceCodeCredentialOptions { TenantId = tenantId }),
            EntraCredentialKind.AzureCli => new AzureCliCredential(
                new AzureCliCredentialOptions { TenantId = tenantId }),
            EntraCredentialKind.Default => new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { TenantId = tenantId }),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile.EntraCredential, "Unknown credential kind."),
        };
    }
}
