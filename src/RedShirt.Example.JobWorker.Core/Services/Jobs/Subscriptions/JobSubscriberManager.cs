using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

internal interface IJobSubscriberManager : IHandlerSubComponent;

internal class JobSubscriberManager(
    IJobSource jobSource,
    IJobSubscriberIntakeQueue jobSubscriberIntakeQueue,
    IJobIntakeService jobIntakeService) : IJobSubscriberManager
{
    public async Task<HandlerComponentResponse> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!jobSource.IsSubscriptionSource)
        {
            return HandlerComponentResponse.NotEnabled;
        }

        await jobSource.StartSubscriberAsync(cancellationToken);

        while (await jobSubscriberIntakeQueue.GetNextAsync(cancellationToken) is { } returnValue)
        {
            await jobIntakeService.SubmitAsync(returnValue, cancellationToken);
        }

        return HandlerComponentResponse.Finished;
    }
}