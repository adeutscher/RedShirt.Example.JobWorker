using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

internal class NoReactionFailureHandler : IJobFailureHandler
{
    public Task HandleFailureAsync(IRawJobModel rawJobModel, FailureType failureType, Exception? exception,
        CancellationToken cancellationToken = default)
    {
        /*
         * No action within this handler.
         * If the DLQ is not considered to be configured for the SQS queue, then the job source's acknowledgement method will attempt to handle poison messages.
         */
        return Task.CompletedTask;
    }
}