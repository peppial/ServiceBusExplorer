namespace SbExplorer.Core.Connections;

/// <summary>Parsed view of a Service Bus connection string, used for validation and display.</summary>
public sealed record ConnectionStringInfo(
    string Endpoint,
    string FullyQualifiedNamespace,
    string? SharedAccessKeyName,
    string? EntityPath,
    bool UseDevelopmentEmulator)
{
    /// <summary>The emulator serves AMQP on 5672 but management (HTTP) on this port.</summary>
    public const int EmulatorManagementPort = 5300;

    /// <summary>
    /// Returns the connection string the administration client should use. For the emulator, an endpoint
    /// without an explicit port is pointed at <see cref="EmulatorManagementPort"/>; everything else is unchanged.
    /// </summary>
    public static string ForAdministration(string connectionString)
    {
        if (!TryParse(connectionString, out var info, out _) || !info!.UseDevelopmentEmulator)
        {
            return connectionString;
        }

        var endpoint = new Uri(info.Endpoint);
        if (!endpoint.IsDefaultPort)
        {
            return connectionString;
        }

        var managementEndpoint = new UriBuilder(endpoint) { Port = EmulatorManagementPort }.Uri.ToString();
        return connectionString.Replace(info.Endpoint, managementEndpoint, StringComparison.Ordinal);
    }

    /// <summary>
    /// The connection string without its EntityPath, so clients aren't tied to one entity. The key's own scope
    /// (namespace or entity policy) still decides what it may access.
    /// </summary>
    public static string WithoutEntityPath(string connectionString) =>
        string.Join(';', connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !segment.StartsWith("EntityPath=", StringComparison.OrdinalIgnoreCase)));

    public static bool TryParse(string? connectionString, out ConnectionStringInfo? info, out string? error)
    {
        info = null;
        error = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error = "Connection string is empty.";
            return false;
        }

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                error = $"Malformed segment '{segment}'.";
                return false;
            }

            parts[segment[..separator].Trim()] = segment[(separator + 1)..].Trim();
        }

        if (!parts.TryGetValue("Endpoint", out var endpoint) || string.IsNullOrEmpty(endpoint))
        {
            error = "Connection string has no Endpoint.";
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            error = $"Endpoint '{endpoint}' is not a valid URI.";
            return false;
        }

        var hasKey = parts.ContainsKey("SharedAccessKey") && parts.ContainsKey("SharedAccessKeyName");
        if (!hasKey && !parts.ContainsKey("SharedAccessSignature"))
        {
            error = "Connection string needs SharedAccessKeyName and SharedAccessKey, or SharedAccessSignature.";
            return false;
        }

        info = new ConnectionStringInfo(
            endpoint,
            uri.Host,
            parts.GetValueOrDefault("SharedAccessKeyName"),
            parts.GetValueOrDefault("EntityPath"),
            parts.TryGetValue("UseDevelopmentEmulator", out var emulator) &&
                bool.TryParse(emulator, out var isEmulator) && isEmulator);
        return true;
    }
}
