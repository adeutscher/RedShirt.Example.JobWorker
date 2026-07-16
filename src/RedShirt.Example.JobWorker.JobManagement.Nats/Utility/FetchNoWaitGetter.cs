using NATS.Client.Core;
using NATS.Client.JetStream;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Utility;

internal interface IFetchNoWaitGetter
{
    IAsyncEnumerable<INatsJSMsg<NatsMemoryOwner<byte>>> FetchNoWaitAsync(INatsJSConsumer consumer, NatsJSFetchOpts opts,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Perform the actual NatsJsConsumer.FetchNoWaitAsync call.
///     Written to separate some difficult-to-mock logic from NatsJobSource
///     Cannot seem to mock because of the internal-to-NATs type NatsJSApiResult when creating a consumer.
/// </summary>
internal class FetchNoWaitGetter : IFetchNoWaitGetter
{
    public IAsyncEnumerable<INatsJSMsg<NatsMemoryOwner<byte>>> FetchNoWaitAsync(INatsJSConsumer consumer,
        NatsJSFetchOpts opts, CancellationToken cancellationToken = default)
    {
        // Note: FetchNoWaitAsync is discouraged if not used with a back-off like Core's WorkerLoop does
        return ((NatsJSConsumer) consumer)
            .FetchNoWaitAsync<NatsMemoryOwner<byte>>(opts,
                cancellationToken: cancellationToken);
    }
}