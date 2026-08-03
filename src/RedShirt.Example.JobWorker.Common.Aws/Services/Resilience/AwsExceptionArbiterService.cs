using Amazon.Runtime;
using RedShirt.Example.JobWorker.Common.Aws.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Aws.Services.Resilience;

internal interface IAwsExceptionArbiterService
{
    AwsExceptionArbiterReport GetJudgement(Exception exception);
}

internal class AwsExceptionArbiterService : IAwsExceptionArbiterService
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

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException;
        }

        return exception;
    }

    private static AwsExceptionArbiterReport Fresh(bool isCritical, bool isTransient)
    {
        return new AwsExceptionArbiterReport
        {
            IsCritical = isCritical,
            CouldBeTransient = isTransient
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

    public AwsExceptionArbiterReport GetJudgement(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        exception = Unwrap(exception);

        return exception switch
        {
            // Service returned an HTTP / AWS error. Transient for throttling, timeouts, and 5xx.
            AmazonServiceException serviceException =>
                Fresh(false, IsTransientAmazonServiceException(serviceException)),
            // Client-side AWS SDK failure before a useful service response (often connectivity).
            AmazonClientException => Fresh(false, true),
            HttpRequestException => Fresh(false, true),
            SocketException => Fresh(false, true),
            // HttpClient request timeouts commonly surface as TaskCanceledException; treat as retryable.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(false, true),
            OperationCanceledException => Fresh(false, false),
            ArgumentException => Fresh(false, false),
            // Unrecognized exception type — treat as critical so callers surface the raw failure.
            _ => Fresh(true, false)
        };
    }
}