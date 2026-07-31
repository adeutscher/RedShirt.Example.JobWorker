using Azure;
using Azure.Identity;
using RedShirt.Example.JobWorker.Common.Azure.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Azure.Services;

internal interface IAzureExceptionArbiterService
{
    AzureExceptionArbiterReport GetJudgement(Exception exception);
}

internal class AzureExceptionArbiterService : IAzureExceptionArbiterService
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

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException {InnerExceptions.Count: 1} aggregate
               && aggregate.InnerException is not null)
        {
            exception = aggregate.InnerException;
        }

        return exception;
    }

    private static AzureExceptionArbiterReport Fresh(bool isExpected, bool isTransient)
    {
        return new AzureExceptionArbiterReport
        {
            IsExpected = isExpected,
            CouldBeTransient = isTransient
        };
    }

    public AzureExceptionArbiterReport GetJudgement(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        exception = Unwrap(exception);

        return exception switch
        {
            // Service returned an HTTP error (or no response). Transient only for retryable statuses
            // such as timeout, throttling, and 5xx — not for 401/403/404 and other permanent failures.
            RequestFailedException requestFailed =>
                Fresh(true, TransientRequestStatuses.Contains(requestFailed.Status)),
            // Interactive sign-in is required; a worker process cannot recover by retrying.
            AuthenticationRequiredException => Fresh(true, false),
            // No credential in the DefaultAzureCredential chain succeeded. Often misconfiguration,
            // but also brief IMDS/startup unavailability — allow retry.
            CredentialUnavailableException => Fresh(true, true),
            // A credential was found but token acquisition failed. Often permanent config/RBAC,
            // but token endpoint blips are possible — allow retry.
            AuthenticationFailedException => Fresh(true, true),
            // Transport-level HTTP failure before a useful Azure response (DNS, TLS, connection drop).
            HttpRequestException => Fresh(true, true),
            // Low-level network failure; typically intermittent connectivity.
            SocketException => Fresh(true, true),
            // HttpClient request timeouts commonly surface as TaskCanceledException; treat as retryable.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true),
            // Explicit CancellationToken cancellation from the caller — do not retry.
            OperationCanceledException => Fresh(true, false),
            // Invalid Azure resource URL (e.g. KeyVaultUrl from configuration) — not retryable.
            // UriFormatException derives from FormatException, not ArgumentException.
            UriFormatException => Fresh(true, false),
            // Client-side argument validation from the Azure SDK / factory (includes ArgumentNullException).
            ArgumentException => Fresh(true, false),
            // Unrecognized exception type — not treated as a known Azure client failure.
            _ => Fresh(false, false)
        };
    }
}