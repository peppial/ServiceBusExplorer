using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace SbExplorer.App.ViewModels;

internal static class ErrorMessages
{
    /// <summary>Turns SDK exceptions into one readable line; SDK messages often carry multi-line diagnostics.</summary>
    public static string Describe(Exception ex)
    {
        var message = ex switch
        {
            CredentialUnavailableException => $"Credential unavailable: {ex.Message}",
            AuthenticationFailedException => $"Sign-in failed: {ex.Message}",
            RequestFailedException { Status: 401 } => "Unauthorized. Check the key, or that your account has a Service Bus Data Owner role.",
            RequestFailedException { Status: 403 } => "Forbidden. The credential lacks Manage rights on this namespace.",
            ServiceBusException sb => sb.Message,
            AggregateException { InnerExceptions.Count: > 0 } agg => Describe(agg.InnerExceptions[^1]),
            _ => ex.Message,
        };

        var firstLine = message.Split('\n', 2)[0].Trim();
        return firstLine.Length > 300 ? firstLine[..300] + "…" : firstLine;
    }
}
