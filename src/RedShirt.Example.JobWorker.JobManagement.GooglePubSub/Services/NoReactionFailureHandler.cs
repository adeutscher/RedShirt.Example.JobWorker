using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        // No action; leave error handling to poison-message enforcement and/or the subscription's
        // max-delivery / dead-letter settings
        return Task.CompletedTask;
    }
}