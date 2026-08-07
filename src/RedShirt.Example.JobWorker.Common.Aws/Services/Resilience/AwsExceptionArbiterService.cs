using Amazon.Runtime;
using RedShirt.Example.JobWorker.Common.Aws.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;

internal interface IAwsExceptionArbiterService
{
    AwsExceptionArbiterReport GetReport(Exception exception);
}

internal sealed class AwsExceptionArbiterService : IAwsExceptionArbiterService
{
    private static readonly HashSet<int> TransientRequestStatuses =
    [
        0, // no HTTP response received
        408,
        429,
        500,
        502,
        503,
        504
    ];

    private static readonly HashSet<string> TransientErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Throttling",
        "ThrottlingException",
        "RequestLimitExceeded",
        "ProvisionedThroughputExceededException",
        "SlowDown",
        "ServiceUnavailable",
        "InternalFailure",
        "InternalError",
        "RequestTimeout",
        "PriorRequestNotComplete",
        "Timeout",
        "TimeoutError"
    };

    private static AwsExceptionArbiterReport Fresh(bool isExpected, bool isTransient, bool couldBeExternallySolvable)
    {
        return new AwsExceptionArbiterReport
        {
            IsExpected = isExpected,
            CouldBeTransient = isTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static bool IsTransientAmazonServiceException(AmazonServiceException serviceException)
    {
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (TransientErrorCodes.Contains(serviceException.ErrorCode))
        {
            return true;
        }

        return TransientRequestStatuses.Contains((int) serviceException.StatusCode);
    }

    // Externally solvable whenever the failure is transient (infra can be fixed/scaled) or the
    // service returned auth/not-found (401/403/404), which ops can resolve via IAM/resource changes.
    private static bool CouldBeExternallySolvableAmazonServiceException(AmazonServiceException serviceException)
    {
        return IsTransientAmazonServiceException(serviceException)
               || (int) serviceException.StatusCode is 401 or 403 or 404;
    }

    public AwsExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Unwrap
        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            // Service returned an HTTP / AWS error. Transient for throttling, timeouts, and 5xx.
            AmazonServiceException serviceException =>
                Fresh(
                    true,
                    IsTransientAmazonServiceException(serviceException),
                    CouldBeExternallySolvableAmazonServiceException(serviceException)),
            // Client-side AWS SDK failure before a useful service response (often connectivity).
            AmazonClientException => Fresh(true, true, true),
            HttpRequestException => Fresh(true, true, true),
            SocketException => Fresh(true, true, true),
            // HttpClient request timeouts commonly surface as TaskCanceledException; treat as retryable.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true, true),
            // Explicit CancellationToken cancellation from the caller — not externally solvable.
            OperationCanceledException => Fresh(true, false, false),
            ArgumentException => Fresh(true, false, false),
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}