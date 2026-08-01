using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

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
    public async Task<HandlerResponseEnum> RunAsync(CancellationToken cancellationToken = default)
    {
        var policyLoop = Policy.Handle<ReasonToWaitException>(_ => executionEndArbiter.ShouldKeepRunning())
            .RetryForeverAsync(async (_, retryAttempt) =>
            {
                // Exponential back-off, to the cap of a configurable amount
                var span = TimeSpan.FromSeconds(Math.Min(loopOptions.Value.EffectiveMaxIdleWaitSeconds,
                    Math.Pow(2, retryAttempt)));
                logger.LogTrace("Waiting before pulling more jobs, retrying in {Span} s",
                    span.TotalSeconds);
                await sleepService.DelayAsync(span, cancellationToken);
            });

        try
        {
            jobLoaderStateService.ReportLoaderStart();
            while (executionEndArbiter.ShouldKeepRunning())
            {
                await policyLoop.ExecuteAsync(jobLoader.RunAsync, cancellationToken);
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