using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Models;
using RedShirt.Example.JobWorker.Connectors.Common.Http.Exceptions;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;

/// <summary>
///     Classifies Bar connector exceptions for retry decisions.
/// </summary>
internal interface IBarExceptionArbiterService
{
    BarExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Bar-oriented exception arbiter modelled after the MySQL / Azure exception arbiters:
///     known infrastructure and retryable HTTP failures may be transient; caller cancel and bad arguments are not.
/// </summary>
internal sealed class BarExceptionArbiterService : IBarExceptionArbiterService
{
    private static readonly HashSet<int> TransientHttpStatuses =
    [
        408,
        429,
        500,
        502,
        503,
        504
    ];

    private static BarExceptionArbiterReport Fresh(bool isExpected, bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new BarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static BarExceptionArbiterReport Handled(
        bool isExpected,
        bool couldBeTransient,
        bool couldBeExternallySolvable)
    {
        return new BarExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsExpected = isExpected,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
    }

    private static BarExceptionArbiterReport ClassifyHttpRequestException(HttpRequestException exception)
    {
        if (exception.StatusCode is null)
        {
            return Fresh(true, true, true);
        }

        var status = (int) exception.StatusCode.Value;
        if (TransientHttpStatuses.Contains(status))
        {
            return Fresh(true, true, true);
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (exception.StatusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound)
        {
            return Fresh(true, false, true);
        }

        return Fresh(true, false, false);
    }

    public BarExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException!;
        }

        return exception switch
        {
            /*
             * BarReasonToWaitException is a special case. Functionally, it's absolutely transient.
             * In fact, it's literally implied in the name: "you have a reason to wait, and then things could be better".
             *
             * However, the reason that it's a special case is that the exception is expected to be caught and respected
             * for as long as necessary for the API request to go through.
             */
            BarReasonToWaitException => Fresh(true, false, true),
            OAuthRequestException {StatusCode: HttpStatusCode.Unauthorized} => Fresh(true, false, true),
            OAuthRequestException => Fresh(true, true, true),
            OAuthRequestJsonException => Fresh(true, false, false),
            BarRecordNotFoundException => Fresh(true, false, false),
            BarUnauthorizedException => Fresh(true, false, true),
            BarException w =>
                Handled(true, w is {IsHandled: false, CouldBeTransient: true}, w.CouldBeExternallySolvable),
            WorkerSecretManagerException w =>
                Handled(true, w is {IsHandled: false, CouldBeTransient: true}, w.CouldBeExternallySolvable),
            HttpRequestException http => ClassifyHttpRequestException(http),
            SocketException
                or TimeoutException => Fresh(true, true, true),
            JsonException => Fresh(true, false, false),
            TaskCanceledException => Fresh(true, true, true),
            OperationCanceledException => Fresh(true, false, false),
            ArgumentException => Fresh(true, false, false),
            _ => Fresh(false, false, false)
        };
    }
}