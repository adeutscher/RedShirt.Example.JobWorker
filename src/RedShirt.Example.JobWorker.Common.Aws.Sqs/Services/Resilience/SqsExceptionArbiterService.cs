using Amazon.SQS;
using Amazon.SQS.Model;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Models;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;

internal interface ISqsExceptionArbiterService
{
    SqsExceptionArbiterReport GetJudgement(Exception exception);
}

/// <summary>
///     SQS-oriented arbiter. Classifies SQS-specific failures, then delegates remaining
///     <see cref="AmazonSQSException" /> instances to <see cref="IAwsExceptionArbiterService" />.
///     Unrecognized exception types are marked critical so callers surface them raw.
/// </summary>
internal class SqsExceptionArbiterService(IAwsExceptionArbiterService awsExceptionArbiterService)
    : ISqsExceptionArbiterService
{
    private static SqsExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new SqsExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static SqsExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new SqsExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private SqsExceptionArbiterReport MapFromAws(AmazonSQSException exception)
    {
        var report = awsExceptionArbiterService.GetJudgement(exception);
        return Fresh(report.IsCritical, report.CouldBeTransient);
    }

    public SqsExceptionArbiterReport GetJudgement(Exception exception)
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
            WorkerSqsException workerSqs =>
                Handled(workerSqs.IsCritical, workerSqs is {IsHandled: false, IsTransient: true}),

            /*
             * Strictly speaking, mapping all of these AmazonSQSException inheritors
             * isn't absolutely necessary - the general AmazonServiceException arbiter
             * *should* be able to deduce the problem though status codes.
             * That said, it offers a certain sense of security.
             */

            // --- SQS: transient ---
            RequestThrottledException => Fresh(false, true),
            OverLimitException => Fresh(false, true),
            KmsThrottledException => Fresh(false, true),
            KmsDisabledException => Fresh(false, true),
            PurgeQueueInProgressException => Fresh(false, true),
            QueueDeletedRecentlyException => Fresh(false, true),

            // --- SQS: permanent / caller must recover without blind retry ---
            QueueDoesNotExistException or QueueNameExistsException or ResourceNotFoundException =>
                Fresh(false, false),
            ReceiptHandleIsInvalidException or MessageNotInflightException => Fresh(false, false),
            InvalidMessageContentsException or InvalidAddressException =>
                Fresh(false, false),
            InvalidAttributeNameException or InvalidAttributeValueException or InvalidBatchEntryIdException =>
                Fresh(false, false),
            InvalidSecurityException => Fresh(false, false),
            BatchEntryIdsNotDistinctException or BatchRequestTooLongException or EmptyBatchRequestException
                or TooManyEntriesInBatchRequestException => Fresh(false, false),
            UnsupportedOperationException => Fresh(false, false),
            KmsAccessDeniedException or KmsInvalidKeyUsageException
                or KmsInvalidStateException or KmsNotFoundException or KmsOptInRequiredException =>
                Fresh(false, false),

            // Remaining untyped SQS service errors — shared AWS heuristics.
            AmazonSQSException sqsException => MapFromAws(sqsException),

            // Unrecognized exception type — critical so callers surface the raw failure.
            _ => Fresh(true, false)
        };
    }
}