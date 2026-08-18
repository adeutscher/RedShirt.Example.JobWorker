using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

internal interface IMessageSubscribeSourceStarter : IHandlerSubComponent;

internal class MessageSubscribeSourceStarter(IJobSource jobSource) : IMessageSubscribeSourceStarter
{
    public async Task<HandlerComponentResponse> RunAsync(CancellationToken cancellationToken = default)
    {
        if (jobSource.IsSubscriptionSource)
        {
            await jobSource.StartSubscriberAsync(cancellationToken);
        }

        return HandlerComponentResponse.Bootstrap;
    }
}