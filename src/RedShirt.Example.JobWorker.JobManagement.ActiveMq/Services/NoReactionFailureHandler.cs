using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(
        IRawJobModel rawJobModel,
        Exception? exception,
        CancellationToken cancellationToken = default)
    {
        // No action
        return Task.CompletedTask;
    }
}