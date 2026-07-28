using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(IJobModel jobModel, Exception exception,
        CancellationToken cancellationToken = default)
    {
        /*
         * No action within this handler.
         * Offsets still advance via the job source's batch-complete-before-commit acknowledgement path.
         */
        return Task.CompletedTask;
    }
}