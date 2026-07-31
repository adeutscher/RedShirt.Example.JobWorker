using Confluent.Kafka;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

/// <summary>
///     Classifies Kafka client exceptions for retry decisions.
/// </summary>
internal interface IKafkaExceptionArbiterService
{
    KafkaExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Kafka-oriented exception arbiter modelled after the Azure / Distributed arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class KafkaExceptionArbiterService : IKafkaExceptionArbiterService
{
    private static readonly HashSet<ErrorCode> CriticalErrorCodes =
    [
        ErrorCode.Local_Fatal, // also covered by Error.IsFatal; listed for explicitness
        ErrorCode.Local_Authentication,
        ErrorCode.SaslAuthenticationFailed,
        ErrorCode.TopicAuthorizationFailed,
        ErrorCode.GroupAuthorizationFailed,
        ErrorCode.ClusterAuthorizationFailed,
        ErrorCode.UnsupportedVersion,
        ErrorCode.InvalidRequest,
        ErrorCode.Local_MaxPollExceeded
    ];

    private static readonly HashSet<ErrorCode> TransientErrorCodes =
    [
        ErrorCode.Local_TimedOut,
        ErrorCode.Local_TimedOutQueue,
        ErrorCode.Local_MsgTimedOut,
        ErrorCode.Local_Transport,
        ErrorCode.Local_Resolve,
        ErrorCode.Local_AllBrokersDown,
        ErrorCode.Local_QueueFull,
        ErrorCode.Local_Intr,
        ErrorCode.RequestTimedOut,
        ErrorCode.NetworkException,
        ErrorCode.LeaderNotAvailable,
        ErrorCode.NotLeaderForPartition,
        ErrorCode.BrokerNotAvailable,
        ErrorCode.ReplicaNotAvailable,
        ErrorCode.PreferredLeaderNotAvailable,
        ErrorCode.EligibleLeadersNotAvailable,
        ErrorCode.GroupCoordinatorNotAvailable,
        ErrorCode.NotCoordinatorForGroup,
        ErrorCode.FencedLeaderEpoch,
        ErrorCode.UnknownLeaderEpoch
    ];

    private static KafkaExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new KafkaExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static KafkaExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new KafkaExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    public KafkaExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException;
        }

        return exception switch
        {
            // Already classified/wrapped by an earlier job-source layer — do not wrap again.
            // Only allow further retry when the prior wrapper has not already exhausted retries.
            WorkerJobSourceException workerJobSource =>
                Handled(workerJobSource.IsCritical, workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            // Confluent marks these as explicitly retriable.
            KafkaRetriableException => Fresh(false, true),
            // Critical codes / fatal librdkafka errors surface raw so operators investigate.
            // Remaining known types retry only for infrastructure / leadership blips.
            KafkaException kafka =>
                Fresh(kafka.Error.IsFatal || CriticalErrorCodes.Contains(kafka.Error.Code),
                    TransientErrorCodes.Contains(kafka.Error.Code)),
            TimeoutException or SocketException => Fresh(false, true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(false, true),
            // Explicit CancellationToken cancellation from the caller — do not retry.
            OperationCanceledException => Fresh(false, false),
            // Client-side argument validation — not retryable.
            ArgumentException => Fresh(false, false),
            // Unrecognized exception type — treat as critical so callers surface the raw failure.
            _ => Fresh(true, false)
        };
    }
}