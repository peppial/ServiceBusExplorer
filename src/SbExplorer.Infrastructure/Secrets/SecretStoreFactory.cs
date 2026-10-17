using SbExplorer.Core.Connections;

namespace SbExplorer.Infrastructure.Secrets;

public static class SecretStoreFactory
{
    /// <summary>Picks the OS keychain when one is usable, otherwise the obfuscated file fallback.</summary>
    public static ISecretStore CreateDefault(string dataDirectory)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainSecretStore();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsDpapiSecretStore(dataDirectory);
        }

        if (OperatingSystem.IsLinux() && LinuxSecretToolStore.IsAvailable())
        {
            return new LinuxSecretToolStore();
        }

        return new FileSecretStore(dataDirectory);
    }
}
