using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Core.Services;

public interface IWorkerLoop
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

internal class WorkerLoop(
    IExecutionEndArbiter executionEndArbiter,
    IJobManager jobManager,
    IJobSource jobSource,
    IOptions<WorkerLoop.ConfigurationModel> options) : IWorkerLoop
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        jobManager.Start(cancellationToken);
        try
        {
            await Policy.Handle<NoJobException>(_ => executionEndArbiter.ShouldKeepRunning())
                .WaitAndRetryForeverAsync(retryAttempt =>
                    TimeSpan.FromSeconds(Math.Max(1,
                        Math.Min(options.Value.MaxWaitSeconds, Math.Pow(2, retryAttempt)))))
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
        catch (NoJobException)
        {
            // pass, only thrown to here in the specific case of a SIGTERM.
        }
    }

    public class ConfigurationModel
    {
        public required int MaxWaitSeconds { get; init; }
    }
}