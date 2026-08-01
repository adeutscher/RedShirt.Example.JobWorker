using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services;

internal class CoreFailureHandlerService(ISafeJobAcknowledgementService safeJobAcknowledgementService, IJobFailureHandler jobFailureHandler)
{
    public async Task HandleAsync(IRawJobDataModel rawJobModel)
    {
        if(await safeJobAcknowledgementService.AcknowledgeSafelyAsync(rawJobModel))
    }
}