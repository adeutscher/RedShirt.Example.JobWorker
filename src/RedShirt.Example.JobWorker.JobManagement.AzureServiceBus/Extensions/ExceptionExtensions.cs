using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;

internal static class ExceptionExtensions
{
    public static bool IsPotentialCredentialProblem(this Exception? exception)
    {
        if (exception is WorkerJobSourceException sourceException)
        {
            exception = sourceException.InnerException;
        }

        // Drill into inner exceptions
        for (var current = exception; current is not null; current = current.InnerException)
        {
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (current is UnauthorizedAccessException)
            {
                return true;
            }

            if (current is ServiceBusException {Reason: ServiceBusFailureReason.GeneralError} serviceBusException
                && serviceBusException.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
