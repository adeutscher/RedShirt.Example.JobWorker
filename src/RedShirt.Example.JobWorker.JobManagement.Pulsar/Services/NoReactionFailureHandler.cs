using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        // No action — Pulsar dead-letter / redelivery policy handles undeliverable messages.
        return Task.CompletedTask;
    }
}
