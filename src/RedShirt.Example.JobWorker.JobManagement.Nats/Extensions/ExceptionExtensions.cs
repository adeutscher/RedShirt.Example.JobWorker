using NATS.Client.Core;
using NATS.Client.JetStream;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Extensions;

internal static class ExceptionExtensions
{
    public static bool IsPotentialCredentialProblem(this Exception? exception)
    {
        if (exception is WorkerJobSourceException sourceException)
        {
            exception = sourceException.InnerException;
        }

        // Drill into inner exceptions
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case NatsServerException {IsAuthError: true}:
                case NatsJSApiException api when api.Error.Code is 401 or 403:
                    return true;
            }
        }

        return false;
    }
}
