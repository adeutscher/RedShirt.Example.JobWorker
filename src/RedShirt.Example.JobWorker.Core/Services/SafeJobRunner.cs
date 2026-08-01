using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Ensures a safe execution of a job with no leaked exceptions.
/// </summary>
internal interface ISafeJobRunner
{
    Task<bool> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default);
}

/// <summary>
///     Try/Catch layer around the execution of a job.
///     Triggers internal retries up to amount configured in reaction to <see cref="JobRetryException" />
/// </summary>
/// <param name="jobLogicRunner"></param>
/// <param name="sleepService"></param>
/// <param name="logger"></param>
/// <param name="options"></param>
internal class SafeJobRunner(
    IJobLogicRunner jobLogicRunner,
    ISleepService sleepService,
    ILogger<SafeJobRunner> logger,
    IOptions<SafeJobRunner.ConfigurationModel> options) : ISafeJobRunner
{
    public async Task<bool> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        try
        {
            await Policy.Handle<JobRetryException>()
                .RetryAsync(
                    Math.Max(0, options.Value.InternalRetryCount),
                    async (e, retryAttempt) =>
                    {
                        if (e is JobRetryException {DelayTimeMilliseconds: > 0} retryException)
                        {
                            // User has requested that the job handler wait for a certain amount of time before retrying.
                            // Override normal incremental backoff behaviour
                            await sleepService.DelayAsync(
                                TimeSpan.FromMilliseconds(retryException.DelayTimeMilliseconds),
                                cancellationToken);
                            return;
                        }

                        // Incremental back-off between retries when no explicit delay was requested.
                        await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                            cancellationToken);
                    })
                .ExecuteAsync(() => jobLogicRunner.RunAsync(job, cancellationToken));
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error running job: {EMessage}", e.Message);

            return false;
        }
    }

    public sealed class ConfigurationModel
    {
        /// <summary>
        ///     Number of times that a message can be retried internally.
        ///     Internal retries don't trigger on an ordinary exception, but rather on <see cref="JobRetryException" />
        /// </summary>
        public required int InternalRetryCount { get; init; }
    }
}