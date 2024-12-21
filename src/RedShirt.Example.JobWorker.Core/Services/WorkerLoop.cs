using Microsoft.Extensions.Logging;
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
    ILogger<WorkerLoop> logger,
    IOptions<WorkerLoop.ConfigurationModel> options) : IWorkerLoop
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        jobManager.Start(cancellationToken);
        try
        {
            while (executionEndArbiter.ShouldKeepRunning())
            {
                await Policy.Handle<NoJobException>(_ => executionEndArbiter.ShouldKeepRunning())
                    .WaitAndRetryForeverAsync(retryAttempt =>
                            TimeSpan.FromSeconds(Math.Min(Math.Max(10, options.Value.MaxIdleWaitSeconds),
                                Math.Pow(2, retryAttempt))),
                        (_, span) =>
                        {
                            logger.LogTrace("Received no jobs from source, retrying in {Span} s", span.Seconds);
                        })
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
        }
        catch (NoJobException)
        {
            // pass, only thrown to here in the specific case of a SIGTERM.
        }
    }

    public class ConfigurationModel
    {
        public required int MaxIdleWaitSeconds { get; init; }
    }
}