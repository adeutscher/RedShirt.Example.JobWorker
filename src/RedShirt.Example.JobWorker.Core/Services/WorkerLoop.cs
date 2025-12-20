using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Core.Services;

internal interface IWorkerLoop
{
    Task RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Fetches jobs and passes them along to the Job Manager.
///     If no jobs are retrieved, then back off before trying again
/// </summary>
/// <param name="executionEndArbiter"></param>
/// <param name="jobManager"></param>
/// <param name="jobSource"></param>
/// <param name="logger"></param>
/// <param name="options"></param>
internal class WorkerLoop(
    IExecutionEndArbiter executionEndArbiter,
    IJobManager jobManager,
    IJobSource jobSource,
    ILogger<WorkerLoop> logger,
    IOptions<WorkerLoop.ConfigurationModel> options) : IWorkerLoop
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (executionEndArbiter.ShouldKeepRunning())
            {
                await Policy.Handle<NoJobException>(_ => executionEndArbiter.ShouldKeepRunning())
                    .WaitAndRetryForeverAsync(retryAttempt =>
                            // Exponential back-off, to the cap of a configurable amount
                            TimeSpan.FromSeconds(Math.Min(options.Value.EffectiveMaxIdleWaitSeconds,
                                Math.Pow(2, retryAttempt))),
                        (_, span) =>
                        {
                            logger.LogTrace("Received no jobs from source, retrying in {Span} s", span.TotalSeconds);
                        })
                    .ExecuteAsync(async () =>
                    {
                        var jobResponse = await jobSource.GetJobsAsync(cancellationToken);
                        if (jobResponse.Items.Count == 0)
                        {
                            // Throwing an exception in order to leverage Polly's handling for incremental backoff.
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

    public sealed class ConfigurationModel
    {
        public int EffectiveMaxIdleWaitSeconds => Math.Max(1, MaxIdleWaitSeconds);
        public required int MaxIdleWaitSeconds { get; init; }
    }
}