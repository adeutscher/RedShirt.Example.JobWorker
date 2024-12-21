using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Core.Services;

public class WorkerLoop(
    IExecutionEndArbiter executionEndArbiter,
    IJobManager jobManager,
    IJobSource jobSource,
    IOptions<WorkerLoop.ConfigurationModel> options)
{
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return Policy.Handle<NoJobException>()
            .WaitAndRetryForeverAsync(retryAttempt =>
                TimeSpan.FromSeconds(Math.Max(1, Math.Min(options.Value.MaxWaitSeconds, Math.Pow(2, retryAttempt)))))
            .ExecuteAsync(async () =>
            {
                var jobResponse = await jobSource.GetJobsAsync(cancellationToken);
                if (jobResponse.Items.Count == 0)
                {
                    throw new NoJobException();
                }

                await jobManager.RunAsync(jobResponse, cancellationToken);
            });
    }

    public class ConfigurationModel
    {
        public required int MaxWaitSeconds { get; init; }
    }
}