using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Exceptions;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Safety;

/// <summary>
///     Ensures a safe execution of a job with no thrown exceptions.
///     If an exception is thrown, then it is returned gently in the return object.
/// </summary>
internal interface ISafeJobRunner
{
    Task<SafeJobRunResults> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default);
}

/// <summary>
///     Try/Catch layer around the execution of a job.
///     Triggers internal retries up to amount configured in reaction to <see cref="JobRetryException" />
/// </summary>
/// <param name="jobLogicRunner"></param>
/// <param name="sleepService"></param>
/// <param name="timeBorderWrapperService"></param>
/// <param name="logger"></param>
/// <param name="options"></param>
internal sealed class SafeJobRunner(
    IJobLogicRunner jobLogicRunner,
    ISleepService sleepService,
    ITimeBorderWrapperService timeBorderWrapperService,
    ILogger<SafeJobRunner> logger,
    IOptions<SafeJobRunner.ConfigurationModel> options) : ISafeJobRunner
{
    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;

    /// <summary>
    ///     Creates (once) the retry pipeline: <see cref="JobRetryException" />-driven <c>ShouldHandle</c>,
    ///     zero Polly delay, and backoff performed in <c>OnRetry</c> through <see cref="ISleepService" />.
    /// </summary>
    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= BuildRetryPipeline();
    }

    private ResiliencePipeline BuildRetryPipeline()
    {
        var maxRetryAttempts = Math.Max(0, options.Value.InternalRetryCount);
        var builder = new ResiliencePipelineBuilder();

        // Polly v8 requires MaxRetryAttempts >= 1; with zero configured retries, skip the strategy.
        if (maxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                ShouldHandle = new PredicateBuilder().Handle<JobRetryException>(),
                // Do not use Polly-based delays between attempts
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    if (args.Outcome.Exception is JobRetryException {DelayTimeMilliseconds: > 0} retryException)
                    {
                        // User has requested that the job handler wait for a certain amount of time before retrying.
                        // Override normal incremental backoff behaviour
                        await sleepService.DelayAsync(
                            TimeSpan.FromMilliseconds(retryException.DelayTimeMilliseconds),
                            args.Context.CancellationToken);
                        return;
                    }

                    // Incremental back-off between retries when no explicit delay was requested.
                    // Polly v8 AttemptNumber is 0-based; +1 → 2^1, 2^2, 2^3.
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber + 1)),
                        args.Context.CancellationToken);
                }
            });
        }

        return builder.Build();
    }

    public async Task<SafeJobRunResults> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        try
        {
            // Non-positive values (and null) mean "no per-attempt time limit".
            var maximumTime = options.Value.MaxJobTimeSeconds is { } seconds and > 0
                ? TimeSpan.FromSeconds(seconds)
                : (TimeSpan?) null;

            // Intentional: MaxJobTimeSeconds is applied per Polly attempt, not across the whole
            // retry budget. Each JobRetryException retry enters RunAsync again and gets a fresh
            // time border. Total wall-clock time may therefore approach roughly
            // InternalRetryCount × MaxJobTimeSeconds plus backoff delays, assuming
            // that the downstream job implementation actually leverages JobRetryException.
            var jobResult = await GetRetryPipeline().ExecuteAsync(
                async token => await timeBorderWrapperService.RunAsync(
                    job,
                    maximumTime,
                    jobLogicRunner.RunAsync,
                    token),
                cancellationToken);
            return new SafeJobRunResults
            {
                Result = jobResult switch
                {
                    JobResult.Success => CoreJobResult.Success,
                    JobResult.Failure => CoreJobResult.Failure,
                    _ => CoreJobResult.InvalidData
                },
                Exception = null
            };
        }
        catch (OperationCanceledException e)
        {
            return new SafeJobRunResults
            {
                Result = CoreJobResult.Cancelled,
                Exception = e
            };
        }
        catch (TimeoutException e)
        {
            // Thrown by TimeBorderWrapperService when WaitAsync's time limit expires before the job completes.
            return new SafeJobRunResults
            {
                Result = CoreJobResult.Cancelled,
                Exception = e
            };
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error running job: {EMessage}", e.Message);

            return new SafeJobRunResults
            {
                Result = CoreJobResult.Failure,
                Exception = e
            };
        }
    }

    public sealed class ConfigurationModel
    {
        /// <summary>
        ///     Number of times that a message can be retried internally.
        ///     Internal retries don't trigger on an ordinary exception, but rather on <see cref="JobRetryException" />
        /// </summary>
        public required int InternalRetryCount { get; init; }

        /// <summary>
        ///     Maximum seconds for a single job attempt before its composite cancellation token is cancelled.
        ///     Applied per Polly retry attempt (intentional): each internal retry gets a fresh time border.
        ///     <see langword="null" />, <c>0</c>, and negative values disable the per-attempt time limit.
        /// </summary>
        public required int? MaxJobTimeSeconds { get; init; }
    }
}