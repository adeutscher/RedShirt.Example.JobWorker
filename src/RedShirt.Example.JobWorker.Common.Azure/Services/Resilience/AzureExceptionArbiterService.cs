using Azure;
using Azure.Identity;
using RedShirt.Example.JobWorker.Common.Azure.Models;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.Common.Azure.Services.Resilience;

internal interface IAzureExceptionArbiterService
{
    AzureExceptionArbiterReport GetReport(Exception exception);
}

internal sealed class AzureExceptionArbiterService : IAzureExceptionArbiterService
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

    private static AzureExceptionArbiterReport Fresh(bool isExpected, bool isTransient, bool couldBeExternallySolvable)
    {
        return new AzureExceptionArbiterReport
        {
            IsExpected = isExpected,
            CouldBeTransient = isTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    public AzureExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        exception = Unwrap(exception);

        return exception switch
        {
            // Service returned an HTTP error (or no response). Transient only for retryable statuses
            // such as timeout, throttling, and 5xx — not for 401/403/404 and other permanent failures.
            // Externally solvable whenever the status is transient (ops can fix infra) or the failure
            // is auth/not-found (401/403/404), which ops can resolve via IAM/resource changes.
            RequestFailedException requestFailed =>
                Fresh(
                    true,
                    TransientRequestStatuses.Contains(requestFailed.Status),
                    TransientRequestStatuses.Contains(requestFailed.Status)
                    || requestFailed.Status is 401 or 403 or 404),
            // Interactive sign-in is required; a worker process cannot recover by retrying or by an
            // external infra/IAM change alone.
            AuthenticationRequiredException => Fresh(true, false, false),
            // No credential in the DefaultAzureCredential chain succeeded. Often misconfiguration,
            // but also brief IMDS/startup unavailability — allow retry. Granting/fixing the credential
            // (managed identity, env vars) externally can resolve this without a restart.
            CredentialUnavailableException => Fresh(true, true, true),
            // A credential was found but token acquisition failed. Often permanent config/RBAC,
            // but token endpoint blips are possible — allow retry. RBAC/permission grants are external.
            AuthenticationFailedException => Fresh(true, true, true),
            // Transport-level HTTP failure before a useful Azure response (DNS, TLS, connection drop).
            HttpRequestException => Fresh(true, true, true),
            // Low-level network failure; typically intermittent connectivity.
            SocketException => Fresh(true, true, true),
            // SDK / AMQP client timeouts (distinct from TaskCanceledException).
            TimeoutException => Fresh(true, true, true),
            // HttpClient request timeouts commonly surface as TaskCanceledException; treat as retryable.
            // Must be matched before OperationCanceledException (TCE derives from OCE).
            TaskCanceledException => Fresh(true, true, true),
            // Explicit CancellationToken cancellation from the caller — do not retry; not externally solvable.
            OperationCanceledException => Fresh(true, false, false),
            // Invalid Azure resource URL (e.g. KeyVaultUrl from configuration) — not retryable and requires
            // a config change + restart, not an external fix.
            // UriFormatException derives from FormatException, not ArgumentException.
            UriFormatException => Fresh(true, false, false),
            // Client-side argument validation from the Azure SDK / factory (includes ArgumentNullException).
            ArgumentException => Fresh(true, false, false),
            // Unrecognized exception type — treat as unexpected so callers surface the raw failure.
            _ => Fresh(false, false, false)
        };
    }
}