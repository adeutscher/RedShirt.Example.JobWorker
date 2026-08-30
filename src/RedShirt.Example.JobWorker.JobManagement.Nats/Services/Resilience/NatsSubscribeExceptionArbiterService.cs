using NATS.Client.Core;
using NATS.Client.JetStream;
using System.Net.Sockets;
using IOException = System.IO.IOException;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Services.Resilience;

/// <summary>
///     Classifies NATS connection callback exceptions for the subscribe job source:
///     reconnect, halt-on-failure stop, or accounted-for transient noise.
/// </summary>
internal interface INatsSubscribeExceptionArbiter
{
    bool IsAccountedForAndLikelyTransientError(Exception exception);

    bool IsReasonToReconnect(Exception exception);

    bool IsReasonToStopIfHaltOnFailure(Exception exception);
}

internal class NatsSubscribeExceptionArbiterService : INatsSubscribeExceptionArbiter
{
    public bool IsAccountedForAndLikelyTransientError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case NatsJSProtocolException protocol when protocol.HeaderMessage == NatsHeaders.Messages.NoMessages:
                case ObjectDisposedException:
                    return true;
            }
        }

        return false;
    }

    public bool IsReasonToStopIfHaltOnFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case NatsServerException {IsAuthError: true}:
                case NatsJSApiException api when api.Error.Code is 401 or 403 or 404:
                    return true;
            }
        }

        return false;
    }

    public bool IsReasonToReconnect(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case NatsConnectionFailedException:
                case NatsJSConnectionException:
                case NatsNoRespondersException:
                case NatsNoReplyException:
                case NatsTimeoutException:
                case NatsJSTimeoutException:
                case NatsJSApiNoResponseException:
                case SocketException:
                case IOException:
                    return true;
                case NatsJSApiException api when api.Error.Code is 408 or 429 or >= 500:
                    return true;
            }
        }

        return false;
    }
}
