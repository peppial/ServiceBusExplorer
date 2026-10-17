using Azure;
using Azure.Messaging.ServiceBus;
using SbExplorer.Infrastructure.Connections;

namespace SbExplorer.Infrastructure.Tests;

public class ConnectorTests
{
    public static TheoryData<Exception, bool> Errors => new()
    {
        { new UnauthorizedAccessException("Manage,EntityRead claims required"), true },
        { new RequestFailedException(401, "Unauthorized"), true },
        { new RequestFailedException(403, "Forbidden"), true },
        { new AggregateException(new RequestFailedException(401, "x")), true },
        { new InvalidOperationException("wrapped", new UnauthorizedAccessException()), true },
        { new RequestFailedException(404, "Not found"), false },
        { new ServiceBusException("busy", ServiceBusFailureReason.ServiceBusy), false },
        { new TimeoutException(), false },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Recognizes_management_access_denied(Exception error, bool denied) =>
        Assert.Equal(denied, AzureNamespaceConnector.IsAccessDenied(error));
}
