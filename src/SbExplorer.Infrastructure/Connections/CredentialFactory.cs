using Azure.Core;
using Azure.Identity;
using SbExplorer.Core.Connections;

namespace SbExplorer.Infrastructure.Connections;

public static class CredentialFactory
{
    /// <param name="showDeviceCode">
    /// Shows the device-code sign-in instructions. Without it the SDK prints them to the console, which a desktop
    /// app doesn't have. It must return promptly: the SDK only starts waiting for the sign-in once it has.
    /// </param>
    public static TokenCredential Create(
        ConnectionProfile profile, Func<DeviceCodeInfo, CancellationToken, Task>? showDeviceCode = null)
    {
        var tenantId = string.IsNullOrWhiteSpace(profile.TenantId) ? null : profile.TenantId.Trim();

        return profile.EntraCredential switch
        {
            EntraCredentialKind.InteractiveBrowser => new InteractiveBrowserCredential(
                new InteractiveBrowserCredentialOptions { TenantId = tenantId }),
            EntraCredentialKind.DeviceCode => new DeviceCodeCredential(
                new DeviceCodeCredentialOptions { TenantId = tenantId, DeviceCodeCallback = showDeviceCode }),
            EntraCredentialKind.AzureCli => new AzureCliCredential(
                new AzureCliCredentialOptions { TenantId = tenantId }),
            EntraCredentialKind.Default => new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { TenantId = tenantId }),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile.EntraCredential, "Unknown credential kind."),
        };
    }
}
