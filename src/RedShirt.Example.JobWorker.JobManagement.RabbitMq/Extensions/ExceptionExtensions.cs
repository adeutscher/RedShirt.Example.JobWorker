using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;

public static class ExceptionExtensions
{
    public static bool IsPotentialCredentialProblem(this Exception? exception)
    {
        if (exception is WorkerJobSourceException sourceException)
        {
            // Override judged exception with inner exception
            exception = sourceException.InnerException;
        }

        return exception is AuthenticationFailureException
            or BrokerUnreachableException {InnerException: AuthenticationFailureException}
            or PossibleAuthenticationFailureException
            or BrokerUnreachableException {InnerException: PossibleAuthenticationFailureException};
    }
}