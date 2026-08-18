using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        // No action
        return Task.CompletedTask;
    }
}