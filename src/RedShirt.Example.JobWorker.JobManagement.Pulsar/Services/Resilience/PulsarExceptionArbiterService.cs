using Pulsar.Client.Api;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;
using System.Net.Sockets;
using TimeoutException = Pulsar.Client.Api.TimeoutException;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services.Resilience;

/// <summary>
///     Classifies Pulsar client exceptions for retry decisions.
/// </summary>
internal interface IPulsarExceptionArbiterService
{
    PulsarExceptionArbiterReport GetReport(Exception exception);
}

/// <summary>
///     Pulsar-oriented exception arbiter modelled after the Kafka / Azure / Distributed arbiters:
///     known infrastructure failures may be transient; auth, cancel, and bad arguments are not.
/// </summary>
internal class PulsarExceptionArbiterService : IPulsarExceptionArbiterService
{
    private static PulsarExceptionArbiterReport Fresh(bool isCritical, bool couldBeTransient)
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = false,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    private static PulsarExceptionArbiterReport Handled(bool isCritical, bool couldBeTransient)
    {
        return new PulsarExceptionArbiterReport
        {
            AlreadyHandled = true,
            IsCritical = isCritical,
            CouldBeTransient = couldBeTransient
        };
    }

    public PulsarExceptionArbiterReport GetReport(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        while (exception is AggregateException {InnerExceptions.Count: 1, InnerException: not null} aggregate)
        {
            exception = aggregate.InnerException;
        }

        return exception switch
        {
            WorkerJobSourceException workerJobSource =>
                Handled(workerJobSource.IsCritical, workerJobSource is {IsHandled: false, CouldBeTransient: true}),
            ConnectException or LookupException or TooManyRequestsException
                or ConsumerBusyException or ConsumerAssignException or NotConnectedException
                or MetaStoreHandlerNotReadyException or RequestTimeoutException
                or TimeoutException => Fresh(false, true),
            AuthenticationException or AuthorizationException or GettingAuthenticationDataException
                or NotAllowedException or UnsupportedVersionException or TopicTerminatedException
                or AlreadyClosedException or ConsumerNotFoundException
                or InvalidConfigurationException or InvalidTopicNameException => Fresh(true, false),
            System.TimeoutException or SocketException => Fresh(false, true),
            TaskCanceledException => Fresh(false, true),
            OperationCanceledException => Fresh(false, false),
            ArgumentException => Fresh(false, false),
            _ => Fresh(true, false)
        };
    }
}