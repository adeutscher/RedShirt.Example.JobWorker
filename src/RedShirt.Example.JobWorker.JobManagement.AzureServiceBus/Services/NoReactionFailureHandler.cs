using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(IJobModel jobModel, Exception exception,
        CancellationToken cancellationToken = default)
    {
        // No action, leave error handling to max delivery setting on the service bus
        return Task.CompletedTask;
    }
}