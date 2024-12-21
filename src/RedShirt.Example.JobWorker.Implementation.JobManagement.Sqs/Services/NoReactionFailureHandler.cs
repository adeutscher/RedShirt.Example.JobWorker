using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(IJobModel jobModel, Exception exception,
        CancellationToken cancellationToken = default)
    {
        // No action, leave error handling to DLQ settings on SQS queue
        return Task.CompletedTask;
    }
}