using Azure;
using Azure.Storage.Queues.Models;
using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services.Resilience;

internal interface IAzureQueueStorageExceptionArbiterService
{
    AzureQueueStorageExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Job-source Queue Storage arbiter. Classifies queue-specific
///     <see cref="RequestFailedException" /> error codes, then delegates remaining failures to
///     <see cref="IAzureExceptionArbiterService" />.
/// </summary>
internal class AzureQueueStorageExceptionArbiterService(IAzureExceptionArbiterService azureExceptionArbiterService)
    : IAzureQueueStorageExceptionArbiterService
{
    private static AzureQueueStorageExceptionArbiterReport Fresh(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new AzureQueueStorageExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static AzureQueueStorageExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new AzureQueueStorageExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private AzureQueueStorageExceptionArbiterReport MapFromAzureArbiter(Exception exception)
    {
        var report = azureExceptionArbiterService.GetReport(exception);
        return Fresh(report.IsExpected, report.CouldBeTransient, report.CouldBeExternallySolvable);
    }

    private AzureQueueStorageExceptionArbiterReport ClassifyQueueRequest(RequestFailedException exception)
    {
        if (exception.ErrorCode == QueueErrorCode.InternalError)
        {
            return Fresh(true, true, true);
        }

        if (exception.ErrorCode == QueueErrorCode.QueueNotFound
            || exception.ErrorCode == QueueErrorCode.QueueBeingDeleted
            || exception.ErrorCode == QueueErrorCode.QueueDisabled
            || exception.ErrorCode == QueueErrorCode.AuthorizationFailure
            || exception.ErrorCode == QueueErrorCode.AuthenticationFailed)
        {
            return Fresh(true, false, true);
        }

        if (exception.ErrorCode == QueueErrorCode.MessageNotFound
            || exception.ErrorCode == QueueErrorCode.PopReceiptMismatch
            || exception.ErrorCode == QueueErrorCode.MessageTooLarge)
        {
            return Fresh(true, false, false);
        }

        return MapFromAzureArbiter(exception);
    }

    public AzureQueueStorageExceptionArbiterReport GetReport(Exception exception)
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
            RequestFailedException requestFailed => ClassifyQueueRequest(requestFailed),
            _ => MapFromAzureArbiter(exception)
        };
    }
}
