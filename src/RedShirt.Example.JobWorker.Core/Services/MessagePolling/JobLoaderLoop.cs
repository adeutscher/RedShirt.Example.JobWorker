using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Services.Utility;

namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

/// <summary>
///     Common job loader loop.
/// </summary>
internal interface IJobLoaderLoop : IHandlerSubComponent;

internal class JobLoaderLoop(
    IJobLoaderStateService jobLoaderStateService,
    // Confirming that it is intentional to use the base IExecutionEndArbiter
    IExecutionEndArbiter executionEndArbiter,
    ISleepService sleepService,
    IJobLoader jobLoader,
    IOptions<LoopOptionsConfigurationModel> loopOptions,
    ILogger<JobLoaderLoop> logger) : IJobLoaderLoop
{
    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;

    /// <summary>
    ///     Get cached retry pipeline, declaring it if none is currently cached.
    ///     Retries forever on <see cref="ReasonToWaitException" /> while the arbiter says keep running;
    ///     zero Polly delay with exponential backoff via <see cref="ISleepService" />.
    /// </summary>
    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // Equivalent to Polly v7 RetryForeverAsync
                MaxRetryAttempts = int.MaxValue,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is not ReasonToWaitException)
                    {
                        return PredicateResult.False();
                    }

                    // Re-check arbiter so SIGTERM can let NoJobException escape without sleeping
                    return executionEndArbiter.ShouldKeepRunning()
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                // Do not use Polly-based delays between attempts
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    // AttemptNumber is 0-based; +1 preserves Polly v7 RetryForeverAsync 1-based backoff (2^1, 2^2, …).
                    var span = TimeSpan.FromSeconds(Math.Min(loopOptions.Value.EffectiveMaxIdleWaitSeconds,
                        Math.Pow(2, args.AttemptNumber + 1)));
                    logger.LogTrace("Waiting before pulling more jobs, retrying in {Span} s",
                        span.TotalSeconds);
                    await sleepService.DelayAsync(span, args.Context.CancellationToken);
                }
            })
            .Build();
    }

    public async Task<HandlerResponseEnum> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            jobLoaderStateService.ReportLoaderStart();
            while (executionEndArbiter.ShouldKeepRunning())
            {
                await GetRetryPipeline().ExecuteAsync(
                    async token => await jobLoader.RunAsync(token),
                    cancellationToken);
            }
        }
        catch (AbortJobLoaderLoopException)
        {
            // Using AbortJobLoaderException as an exit-override signal
            // Valid behaviour, pass
        }
        catch (NoJobException)
        {
            // pass, only thrown to here in the specific case of a SIGTERM.
        }
        finally
        {
            jobLoaderStateService.ReportLoaderStop();
        }

        return HandlerResponseEnum.Finished;
    }
}
