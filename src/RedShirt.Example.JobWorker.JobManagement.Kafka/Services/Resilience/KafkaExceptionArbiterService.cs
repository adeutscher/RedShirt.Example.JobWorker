using Confluent.Kafka;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services.Resilience;

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

    // Auth-related fatal/critical codes are the subset of CriticalErrorCodes that ops can resolve
    // externally by granting IAM/ACLs. The remaining critical codes (Local_Fatal, UnsupportedVersion,
    // InvalidRequest, Local_MaxPollExceeded) are client lifecycle / protocol issues that are not.
    private static readonly HashSet<ErrorCode> AuthCriticalErrorCodes =
    [
        ErrorCode.Local_Authentication,
        ErrorCode.SaslAuthenticationFailed,
        ErrorCode.TopicAuthorizationFailed,
        ErrorCode.GroupAuthorizationFailed,
        ErrorCode.ClusterAuthorizationFailed
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

    private static KafkaExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new KafkaExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static KafkaExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new KafkaExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    // Externally solvable when ops can grant IAM/ACLs (auth codes) or when the failure is an
    // infra/leadership blip (transient codes). The remaining fatal/critical codes are client
    // lifecycle / protocol issues that require a local fix, not an external one.
    private static bool CouldBeExternallySolvableKafkaError(Error error)
    {
        if (AuthCriticalErrorCodes.Contains(error.Code))
        {
            return true;
        }

        if (error.IsFatal || CriticalErrorCodes.Contains(error.Code))
        {
            return false;
        }

        return TransientErrorCodes.Contains(error.Code);
    }

    public KafkaExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Already classified/wrapped by an earlier job-source layer — do not wrap again.
            // Only allow further retry when the prior wrapper has not already exhausted retries.
            // Propagate the inner wrapper's own externally-solvable classification.
            WorkerJobSourceException workerJobSource =>
                Handled(
                    true,
                    workerJobSource is {IsHandled: false, CouldBeTransient: true},
                    workerJobSource.CouldBeExternallySolvable),
            // Confluent marks these as explicitly retriable — an infra blip ops can clear.
            KafkaRetriableException => Fresh(true, true, true),
            // Fatal / critical codes are expected but permanent (not transient).
            // Remaining known types retry only for infrastructure / leadership blips.
            KafkaException kafka =>
                Fresh(true,
                    !(kafka.Error.IsFatal || CriticalErrorCodes.Contains(kafka.Error.Code))
                    && TransientErrorCodes.Contains(kafka.Error.Code),
                    CouldBeExternallySolvableKafkaError(kafka.Error)),
            TimeoutException
                or SocketException => Fresh(true, true, true),
            // HttpClient-style timeouts sometimes surface as TaskCanceledException.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true, true),
            // Explicit CancellationToken cancellation from the caller — do not retry; not externally solvable.
            OperationCanceledException => Fresh(true, false, false),
            // Client-side argument validation — not retryable and not externally solvable.
            ArgumentException => Fresh(true, false, false),
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}