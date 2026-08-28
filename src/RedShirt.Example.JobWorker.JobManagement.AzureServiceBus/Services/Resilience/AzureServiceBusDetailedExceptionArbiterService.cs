using Azure.Messaging.ServiceBus;
using System.Net.Sockets;
using IOException = System.IO.IOException;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

/// <summary>
///     Classifies processor / connection callback exceptions for the Azure Service Bus subscribe job source:
///     reconnect, halt-on-failure stop, or accounted-for transient noise.
/// </summary>
internal interface IAzureServiceBusDetailedExceptionArbiter
{
    bool IsAccountedForAndLikelyTransientError(Exception exception);

    bool IsReasonToReconnect(Exception exception);

    bool IsReasonToStopIfHaltOnFailure(Exception exception);
}

internal class AzureServiceBusDetailedExceptionArbiterService : IAzureServiceBusDetailedExceptionArbiter
{
    private static readonly HashSet<ServiceBusFailureReason> ReconnectReasons =
    [
        ServiceBusFailureReason.ServiceTimeout,
        ServiceBusFailureReason.ServiceBusy,
        ServiceBusFailureReason.ServiceCommunicationProblem
    ];

    private static readonly HashSet<ServiceBusFailureReason> HaltReasons =
    [
        ServiceBusFailureReason.MessagingEntityNotFound,
        ServiceBusFailureReason.MessagingEntityDisabled
    ];

    private static readonly HashSet<ServiceBusFailureReason> TransientNoiseReasons =
    [
        ServiceBusFailureReason.MessageLockLost,
        ServiceBusFailureReason.MessageNotFound,
        ServiceBusFailureReason.SessionLockLost
    ];

    public bool IsAccountedForAndLikelyTransientError(Exception exception)
    {
        // Drill into inner exceptions
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case ServiceBusException serviceBus when TransientNoiseReasons.Contains(serviceBus.Reason):
                case ObjectDisposedException:
                    return true;
            }
        }

        return false;
    }

    public bool IsReasonToStopIfHaltOnFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case UnauthorizedAccessException:
                case ServiceBusException serviceBus when HaltReasons.Contains(serviceBus.Reason):
                    return true;
            }
        }

        return false;
    }

    public bool IsReasonToReconnect(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case SocketException:
                case IOException:
                case ServiceBusException serviceBus when ReconnectReasons.Contains(serviceBus.Reason):
                    return true;
                case ServiceBusException
                {
                    Reason: ServiceBusFailureReason.GeneralError, IsTransient: true
                }:
                    return true;
            }
        }

        return false;
    }
}
