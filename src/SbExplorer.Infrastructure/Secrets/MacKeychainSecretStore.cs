using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SbExplorer.Core.Connections;

namespace SbExplorer.Infrastructure.Secrets;

/// <summary>Stores secrets as generic passwords in the user's login keychain.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainSecretStore(string serviceName = "ServiceBusExplorer") : ISecretStore
{
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public bool IsSecure => true;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var status = Find(key, out var length, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }

        ThrowOnError(status, "read");
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            CFRelease(item);
        }
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var status = Find(key, out _, out var data, out var item);
        if (status == ErrSecSuccess)
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            try
            {
                ThrowOnError(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes), "update");
            }
            finally
            {
                CFRelease(item);
            }

            return Task.CompletedTask;
        }

        if (status != ErrSecItemNotFound)
        {
            ThrowOnError(status, "read");
        }

        var service = Encoding.UTF8.GetBytes(serviceName);
        var account = Encoding.UTF8.GetBytes(key);
        status = SecKeychainAddGenericPassword(
            IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, (uint)bytes.Length, bytes, out var added);
        ThrowOnError(status, "add");
        CFRelease(added);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var status = Find(key, out _, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return Task.CompletedTask;
        }

        ThrowOnError(status, "read");
        SecKeychainItemFreeContent(IntPtr.Zero, data);
        try
        {
            ThrowOnError(SecKeychainItemDelete(item), "delete");
        }
        finally
        {
            CFRelease(item);
        }

        return Task.CompletedTask;
    }

    private int Find(string key, out uint length, out IntPtr data, out IntPtr item)
    {
        var service = Encoding.UTF8.GetBytes(serviceName);
        var account = Encoding.UTF8.GetBytes(key);
        return SecKeychainFindGenericPassword(
            IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out length, out data, out item);
    }

    private static void ThrowOnError(int status, string operation)
    {
        if (status != ErrSecSuccess)
        {
            throw new InvalidOperationException($"Keychain {operation} failed with OSStatus {status}.");
        }
    }

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
