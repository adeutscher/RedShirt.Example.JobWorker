using Amazon.SQS;
using Amazon.SQS.Model;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Models;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;

internal interface ISqsExceptionArbiterService
{
    SqsExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     SQS-oriented arbiter. Classifies SQS-specific failures, then delegates remaining
///     <see cref="AmazonSQSException" /> instances to <see cref="IAwsExceptionArbiterService" />.
///     Unrecognized exception types are marked unexpected so callers surface them raw.
/// </summary>
internal class SqsExceptionArbiterService(IAwsExceptionArbiterService awsExceptionArbiterService)
    : ISqsExceptionArbiterService
{
    private static SqsExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static SqsExceptionArbiterReport Handled(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new SqsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private SqsExceptionArbiterReport MapFromAws(AmazonSQSException exception)
    {
        var report = awsExceptionArbiterService.GetReport(exception);
        return Fresh(report.IsExpected, report.CouldBeTransient, report.CouldBeExternallySolvable);
    }

    public SqsExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Unwrap
        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Already classified/wrapped by an earlier SQS layer — do not wrap again.
            // Only allow further retry when the prior wrapper has not already exhausted retries.
            // Propagate the inner wrapper's own externally-solvable classification.
            WorkerSqsException workerSqs =>
                Handled(
                    true,
                    workerSqs is {IsHandled: false, CouldBeTransient: true},
                    workerSqs.CouldBeExternallySolvable),

            /*
             * Strictly speaking, mapping all of these AmazonSQSException inheritors
             * isn't absolutely necessary - the general AmazonServiceException arbiter
             * *should* be able to deduce the problem though status codes.
             * That said, it offers a certain sense of security.
             */

            // --- SQS: transient --- (throttling/busy/KMS-disabled states ops can clear externally)
            RequestThrottledException => Fresh(true, true, true),
            OverLimitException => Fresh(true, true, true),
            KmsThrottledException => Fresh(true, true, true),
            KmsDisabledException => Fresh(true, true, true),
            PurgeQueueInProgressException => Fresh(true, true, true),
            QueueDeletedRecentlyException => Fresh(true, true, true),

            // --- SQS: permanent / caller must recover without blind retry ---
            // Missing queue/resource — ops can create it externally without a worker restart.
            QueueDoesNotExistException
                or ResourceNotFoundException => Fresh(true, false, true),
            // Queue already exists with different attributes — a naming/config conflict, not
            // something ops can resolve by creating a resource.
            QueueNameExistsException => Fresh(true, false, false),
            ReceiptHandleIsInvalidException
                or MessageNotInflightException => Fresh(true, false, false),
            InvalidMessageContentsException
                or InvalidAddressException => Fresh(true, false, false),
            InvalidAttributeNameException
                or InvalidAttributeValueException
                or InvalidBatchEntryIdException => Fresh(true, false, false),
            InvalidSecurityException => Fresh(true, false, false),
            BatchEntryIdsNotDistinctException
                or BatchRequestTooLongException
                or EmptyBatchRequestException
                or TooManyEntriesInBatchRequestException => Fresh(true, false, false),
            UnsupportedOperationException => Fresh(true, false, false),
            // KMS access/state issues ops can grant or fix on the key without a worker restart.
            KmsAccessDeniedException
                or KmsInvalidStateException
                or KmsNotFoundException
                or KmsOptInRequiredException => Fresh(true, false, true),
            // Key exists but its KeyUsage doesn't match the requested operation — a configuration
            // mismatch in which key is used, not something ops can fix on the key itself.
            KmsInvalidKeyUsageException => Fresh(true, false, false),

            // Remaining untyped SQS service errors — shared AWS heuristics.
            AmazonSQSException sqsException => MapFromAws(sqsException),

            // Unrecognized exception type — unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}