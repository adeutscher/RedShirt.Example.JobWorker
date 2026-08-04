using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Runtime;
using RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using DynamoDbInternalServerErrorException = Amazon.DynamoDBv2.Model.InternalServerErrorException;
using DynamoDbLimitExceededException = Amazon.DynamoDBv2.Model.LimitExceededException;
using DynamoDbProvisionedThroughputExceededException =
    Amazon.DynamoDBv2.Model.ProvisionedThroughputExceededException;
using DynamoDbResourceInUseException = Amazon.DynamoDBv2.Model.ResourceInUseException;
using DynamoDbResourceNotFoundException = Amazon.DynamoDBv2.Model.ResourceNotFoundException;
using KinesisInternalFailureException = Amazon.Kinesis.Model.InternalFailureException;
using KinesisLimitExceededException = Amazon.Kinesis.Model.LimitExceededException;
using KinesisProvisionedThroughputExceededException =
    Amazon.Kinesis.Model.ProvisionedThroughputExceededException;
using KinesisResourceInUseException = Amazon.Kinesis.Model.ResourceInUseException;
using KinesisResourceNotFoundException = Amazon.Kinesis.Model.ResourceNotFoundException;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

internal interface IKinesisExceptionArbiterService
{
    KinesisExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Kinesis job-source arbiter. Classifies Kinesis- and DynamoDB-specific failures,
///     then delegates remaining <see cref="AmazonKinesisException" /> /
///     <see cref="AmazonDynamoDBException" /> instances to <see cref="IAwsExceptionArbiterService" />.
///     Also recognizes underlying worker exception types
///     (<see cref="WorkerJobSourceException" />, <see cref="WorkerSqsException" />,
///     <see cref="WorkerDistributedException" />) as already handled, allowing further
///     retry only when those wrappers have not already exhausted retries.
///     Unrecognized exception types are marked unexpected so callers surface them raw.
/// </summary>
internal class KinesisExceptionArbiterService(IAwsExceptionArbiterService awsExceptionArbiterService)
    : IKinesisExceptionArbiterService
{
    private static KinesisExceptionArbiterReport Fresh(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static KinesisExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private KinesisExceptionArbiterReport MapFromAws(AmazonServiceException exception)
    {
        var report = awsExceptionArbiterService.GetReport(exception);
        return Fresh(report.IsExpected, report.CouldBeTransient, report.CouldBeExternallySolvable);
    }

    public KinesisExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // --- Underlying worker exceptions
            WorkerJobSourceException w =>
                Handled(true, w is {IsHandled: false, CouldBeTransient: true}, w.CouldBeExternallySolvable),
            WorkerSqsException w =>
                Handled(true, w is {IsHandled: false, CouldBeTransient: true}, w.CouldBeExternallySolvable),
            WorkerDistributedException w =>
                Handled(true, w is {IsHandled: false, CouldBeTransient: true}, w.CouldBeExternallySolvable),

            /*
             * Strictly speaking, mapping all of these Kinesis/DynamoDB inheritors
             * isn't absolutely necessary - the general AmazonServiceException arbiter
             * *should* be able to deduce the problem though status codes.
             * That said, it offers a certain sense of security.
             */

            // --- Kinesis: transient --- (throughput/throttling/internal/KMS-throttling clears externally)
            KinesisProvisionedThroughputExceededException => Fresh(true, true, true),
            KinesisLimitExceededException => Fresh(true, true, true),
            KinesisInternalFailureException => Fresh(true, true, true),
            KMSThrottlingException => Fresh(true, true, true),
            KMSDisabledException => Fresh(true, true, true),

            // --- Kinesis: permanent / caller must recover without blind retry ---
            // Expired iterators/tokens require the client to re-fetch a fresh iterator; retrying (or
            // an external fix) will not help since the fix is process-local.
            ExpiredIteratorException or ExpiredNextTokenException => Fresh(true, false, false),
            // IAM/permissions — ops can grant access externally without a worker restart.
            AccessDeniedException => Fresh(true, false, true),
            // Bad local arguments/config — not retryable and not an external fix.
            // ReSharper disable once RedundantNameQualifier
            InvalidArgumentException => Fresh(true, false, false),
            ValidationException => Fresh(true, false, false),
            // Missing stream — ops can create it externally without a worker restart.
            KinesisResourceNotFoundException => Fresh(true, false, true),
            // Stream already exists / in use — a naming/lifecycle conflict, not something ops resolve
            // by creating a resource.
            KinesisResourceInUseException => Fresh(true, false, false),
            // KMS access/state issues ops can grant or fix on the key without a worker restart.
            KMSAccessDeniedException => Fresh(true, false, true),
            KMSInvalidStateException
                or KMSNotFoundException or KMSOptInRequiredException => Fresh(true, false, true),
            KinesisEventStreamException => Fresh(true, true, true),
            AmazonKinesisException kinesisException => MapFromAws(kinesisException),

            // --- DynamoDB: transient --- (throughput/throttling/internal issues clear externally)
            DynamoDbProvisionedThroughputExceededException => Fresh(true, true, true),
            RequestLimitExceededException => Fresh(true, true, true),
            ThrottlingException => Fresh(true, true, true),
            DynamoDbInternalServerErrorException => Fresh(true, true, true),
            DynamoDbLimitExceededException => Fresh(true, true, true),
            TransactionConflictException or TransactionInProgressException => Fresh(true, true, true),
            ReplicatedWriteConflictException => Fresh(true, true, true),
            DynamoDbResourceInUseException => Fresh(true, true, true),

            // --- DynamoDB: permanent ---
            // A condition mismatch is a client-side logic/data issue, not an external fix.
            ConditionalCheckFailedException => Fresh(true, false, false),
            // Missing table — ops can create it externally without a worker restart.
            DynamoDbResourceNotFoundException => Fresh(true, false, true),
            TableNotFoundException => Fresh(true, false, true),
            // Table already exists / in use — a naming/lifecycle conflict, not an external resource fix.
            TableAlreadyExistsException or TableInUseException => Fresh(true, false, false),
            // Explicit transaction cancellation — local logic, not an external fix.
            TransactionCanceledException => Fresh(true, false, false),
            // Item/partition too large for the schema — a data-model issue, not an external fix.
            ItemCollectionSizeLimitExceededException => Fresh(true, false, false),
            AmazonDynamoDBException dynamoDbException => MapFromAws(dynamoDbException),

            // Unrecognized exception type — unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}