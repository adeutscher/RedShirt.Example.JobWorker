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
///     Unrecognized exception types are marked critical so callers surface them raw.
/// </summary>
internal class KinesisExceptionArbiterService(IAwsExceptionArbiterService awsExceptionArbiterService)
    : IKinesisExceptionArbiterService
{
    private static KinesisExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static KinesisExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new KinesisExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private KinesisExceptionArbiterReport MapFromAws(AmazonServiceException exception)
    {
        var report = awsExceptionArbiterService.GetJudgement(exception);
        return Fresh(report.IsCritical, report.CouldBeTransient);
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
            WorkerJobSourceException workerJobSource =>
                Handled(workerJobSource.IsCritical,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            WorkerSqsException workerSqs =>
                Handled(workerSqs.IsCritical, workerSqs is {IsHandled: false, IsTransient: true}),
            WorkerDistributedException workerDistributed =>
                Handled(workerDistributed.IsCritical, workerDistributed.IsTransient),

            /*
             * Strictly speaking, mapping all of these Kinesis/DynamoDB inheritors
             * isn't absolutely necessary - the general AmazonServiceException arbiter
             * *should* be able to deduce the problem though status codes.
             * That said, it offers a certain sense of security.
             */

            // --- Kinesis: transient ---
            KinesisProvisionedThroughputExceededException => Fresh(false, true),
            KinesisLimitExceededException => Fresh(false, true),
            KinesisInternalFailureException => Fresh(false, true),
            KMSThrottlingException => Fresh(false, true),
            KMSDisabledException => Fresh(false, true),

            // --- Kinesis: permanent / caller must recover without blind retry ---
            // Expired iterators need a fresh GetShardIterator, not retries of the same call.
            ExpiredIteratorException or ExpiredNextTokenException => Fresh(false, false),
            AccessDeniedException => Fresh(true, false),
            // ReSharper disable once RedundantNameQualifier
            InvalidArgumentException => Fresh(false, false),
            ValidationException => Fresh(false, false),
            KinesisResourceNotFoundException => Fresh(true, false),
            KinesisResourceInUseException => Fresh(false, false),
            KMSAccessDeniedException => Fresh(true, false),
            KMSInvalidStateException
                or KMSNotFoundException or KMSOptInRequiredException => Fresh(false, false),
            KinesisEventStreamException => Fresh(false, true),
            AmazonKinesisException kinesisException => MapFromAws(kinesisException),

            // --- DynamoDB: transient ---
            DynamoDbProvisionedThroughputExceededException => Fresh(false, true),
            RequestLimitExceededException => Fresh(false, true),
            ThrottlingException => Fresh(false, true),
            DynamoDbInternalServerErrorException => Fresh(false, true),
            DynamoDbLimitExceededException => Fresh(false, true),
            TransactionConflictException or TransactionInProgressException => Fresh(false, true),
            ReplicatedWriteConflictException => Fresh(false, true),
            DynamoDbResourceInUseException => Fresh(false, true),

            // --- DynamoDB: permanent ---
            ConditionalCheckFailedException => Fresh(false, false),
            DynamoDbResourceNotFoundException => Fresh(false, false),
            TableNotFoundException => Fresh(true, false),
            TableAlreadyExistsException or TableInUseException => Fresh(false, false),
            TransactionCanceledException => Fresh(false, false),
            ItemCollectionSizeLimitExceededException => Fresh(false, false),
            AmazonDynamoDBException dynamoDbException => MapFromAws(dynamoDbException),

            // Unrecognized exception type — critical so callers surface the raw failure.
            _ => Fresh(true, false)
        };
    }
}