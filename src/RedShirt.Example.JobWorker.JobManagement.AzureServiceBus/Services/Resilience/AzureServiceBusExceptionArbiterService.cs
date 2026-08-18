using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services.Resilience;

internal interface IAzureServiceBusExceptionArbiterService
{
    AzureServiceBusExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Job-source Service Bus arbiter. Classifies <see cref="ServiceBusException" />, then delegates
///     remaining failures to <see cref="IAzureExceptionArbiterService" />.
/// </summary>
internal class AzureServiceBusExceptionArbiterService(IAzureExceptionArbiterService azureExceptionArbiterService)
    : IAzureServiceBusExceptionArbiterService
{
    private static readonly HashSet<ServiceBusFailureReason> TransientReasons =
    [
        ServiceBusFailureReason.ServiceTimeout,
        ServiceBusFailureReason.ServiceBusy,
        ServiceBusFailureReason.ServiceCommunicationProblem,
        ServiceBusFailureReason.QuotaExceeded
    ];

    private static readonly HashSet<ServiceBusFailureReason> ExternallySolvablePermanentReasons =
    [
        ServiceBusFailureReason.MessagingEntityNotFound,
        ServiceBusFailureReason.MessagingEntityDisabled
    ];

    private static AzureServiceBusExceptionArbiterReport Fresh(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new AzureServiceBusExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static AzureServiceBusExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new AzureServiceBusExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private AzureServiceBusExceptionArbiterReport MapFromAzureArbiter(Exception exception)
    {
        var report = azureExceptionArbiterService.GetReport(exception);
        return Fresh(report.IsExpected, report.CouldBeTransient, report.CouldBeExternallySolvable);
    }

    private static AzureServiceBusExceptionArbiterReport ClassifyServiceBus(ServiceBusException exception)
    {
        if (TransientReasons.Contains(exception.Reason))
        {
            return Fresh(true, true, true);
        }

        if (ExternallySolvablePermanentReasons.Contains(exception.Reason))
        {
            return Fresh(true, false, true);
        }

        // MessageLockLost, MessageNotFound, MessageSizeExceeded, SessionCannotBeLocked,
        // SessionLockLost, MessagingEntityAlreadyExists: expected, not retryable, not an ops fix.
        // GeneralError: honour the SDK's own transient flag.
        if (exception.Reason == ServiceBusFailureReason.GeneralError)
        {
            return Fresh(true, exception.IsTransient, exception.IsTransient);
        }

        return Fresh(true, false, false);
    }

    public AzureServiceBusExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Propagate the inner wrapper's own externally-solvable classification.
            WorkerJobSourceException workerJobSource =>
                Handled(
                    true,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true},
                    workerJobSource.CouldBeExternallySolvable),
            WorkerAzureException workerAzure =>
                Handled(
                    true,
                    workerAzure is {IsHandled: false, CouldBeTransient: true},
                    workerAzure.CouldBeExternallySolvable),
            ServiceBusException serviceBus => ClassifyServiceBus(serviceBus),
            // IAM / SAS / RBAC denial — ops can grant access without a worker restart.
            UnauthorizedAccessException => Fresh(true, false, true),
            _ => MapFromAzureArbiter(exception)
        };
    }
}
