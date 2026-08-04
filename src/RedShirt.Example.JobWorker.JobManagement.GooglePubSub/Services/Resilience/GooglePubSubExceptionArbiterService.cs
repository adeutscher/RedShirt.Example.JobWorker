using Grpc.Core;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services.Resilience;

/// <summary>
///     Classifies Google Pub/Sub client exceptions for retry decisions.
/// </summary>
internal interface IGooglePubSubExceptionArbiterService
{
    GooglePubSubExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Pub/Sub-oriented exception arbiter modelled after the Azure / Distributed / Kafka arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class GooglePubSubExceptionArbiterService : IGooglePubSubExceptionArbiterService
{
    private static readonly HashSet<StatusCode> TransientStatusCodes =
    [
        StatusCode.Unavailable,
        StatusCode.DeadlineExceeded,
        StatusCode.ResourceExhausted,
        StatusCode.Aborted,
        StatusCode.Internal,
        StatusCode.Unknown
    ];

    private static readonly HashSet<StatusCode> PermanentStatusCodes =
    [
        StatusCode.InvalidArgument,
        StatusCode.NotFound,
        StatusCode.AlreadyExists,
        StatusCode.PermissionDenied,
        StatusCode.Unauthenticated,
        StatusCode.FailedPrecondition,
        StatusCode.OutOfRange,
        StatusCode.Unimplemented,
        StatusCode.DataLoss
    ];

    // Subset of PermanentStatusCodes that ops can resolve externally: granting IAM (PermissionDenied /
    // Unauthenticated) or creating the missing resource (NotFound). The rest (InvalidArgument,
    // AlreadyExists, FailedPrecondition, OutOfRange, Unimplemented, DataLoss) are caller/config issues.
    private static readonly HashSet<StatusCode> ExternallySolvablePermanentStatusCodes =
    [
        StatusCode.PermissionDenied,
        StatusCode.Unauthenticated,
        StatusCode.NotFound
    ];

    private static readonly HashSet<SocketError> DnsSocketErrors =
    [
        SocketError.HostNotFound,
        SocketError.NoData,
        SocketError.NoRecovery,
        SocketError.TryAgain
    ];

    private static GooglePubSubExceptionArbiterReport Fresh(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static GooglePubSubExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new GooglePubSubExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static GooglePubSubExceptionArbiterReport ClassifyRpcException(RpcException rpc)
    {
        if (ExternallySolvablePermanentStatusCodes.Contains(rpc.StatusCode))
        {
            return Fresh(true, false, true);
        }

        if (PermanentStatusCodes.Contains(rpc.StatusCode))
        {
            return Fresh(true, false, false);
        }

        // Misconfigured host / DNS failure surfaces as Unavailable + "Error connecting to subchannel."
        // with an underlying DNS SocketException. Retrying will not help, and it's a local config
        // issue (bad host), not something ops can fix externally.
        if (rpc.StatusCode == StatusCode.Unavailable && IsUnavailableDnsSubchannelFailure(rpc))
        {
            return Fresh(true, false, false);
        }

        var isTransient = TransientStatusCodes.Contains(rpc.StatusCode);
        return Fresh(true, isTransient, isTransient);
    }

    private static bool IsUnavailableDnsSubchannelFailure(RpcException rpc)
    {
        return IsSubchannelConnectionDetail(rpc.Status.Detail)
               && FindSocketException(rpc) is { } socket
               && DnsSocketErrors.Contains(socket.SocketErrorCode);
    }

    private static bool IsSubchannelConnectionDetail(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return false;
        }

        // grpc-dotnet typically uses "Error connecting to subchannel." (no hyphen).
        return detail.Contains("connecting to subchannel", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("connecting to sub-channel", StringComparison.OrdinalIgnoreCase);
    }

    private static SocketException? FindSocketException(RpcException rpc)
    {
        foreach (var root in (Exception?[]) [rpc.InnerException, rpc.Status.DebugException])
        {
            for (var current = root; current is not null; current = current.InnerException)
            {
                if (current is SocketException socket)
                {
                    return socket;
                }
            }
        }

        return null;
    }

    public GooglePubSubExceptionArbiterReport GetReport(Exception exception)
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
            // gRPC status from the Pub/Sub client / emulator.
            RpcException rpc => ClassifyRpcException(rpc),
            TimeoutException
                or SocketException
                or HttpRequestException => Fresh(true, true, true),
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