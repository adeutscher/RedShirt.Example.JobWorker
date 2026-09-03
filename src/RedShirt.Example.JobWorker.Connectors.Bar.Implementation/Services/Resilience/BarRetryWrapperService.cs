using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;

/// <summary>
///     Retries Bar connector operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="BarException" />.
/// </summary>
internal interface IBarRetryWrapperService
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);

    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

/// <summary>
///     Polly v8-based retry wrapper for Bar connector calls.
///     Retries when <see cref="IBarExceptionArbiterService" /> reports an expected transient failure,
///     using exponential backoff via <see cref="ISleepService" />.
///     <see cref="BarReasonToWaitException" /> instances are not retried here; they propagate to
///     <see cref="BarConnector" /> for indefinite respectful waiting.
/// </summary>
internal sealed class BarRetryWrapperService(
    IBarExceptionArbiterService exceptionArbiterService,
    ILogger<BarRetryWrapperService> logger,
    ISleepService sleepService,
    IOptions<BarRetryWrapperService.ConfigurationModel> options)
    : IBarRetryWrapperService
{
    private const int DefaultRetryCount = 3;

    private ResiliencePipeline? _retryPipeline;

    private ResiliencePipeline GetRetryPipeline()
    {
        return _retryPipeline ??= new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.Value.EffectiveRetryCount,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is not { } exception)
                    {
                        return PredicateResult.False();
                    }

                    if (args.Context.CancellationToken.IsCancellationRequested)
                    {
                        return PredicateResult.False();
                    }

                    if (exception is BarReasonToWaitException)
                    {
                        return PredicateResult.False();
                    }

                    var report = exceptionArbiterService.GetReport(exception);
                    return report is {IsExpected: true, CouldBeTransient: true}
                        ? PredicateResult.True()
                        : PredicateResult.False();
                },
                DelayGenerator = static _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Retrying Bar connector operation after attempt {AttemptNumber}",
                        args.AttemptNumber);
                    await sleepService.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber)),
                        args.Context.CancellationToken);
                }
            })
            .Build();
    }

    private bool TryGetWrappedException(Exception exception, out Exception? wrappedException)
    {
        wrappedException = null;

        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (exception is BarRecordNotFoundException)
        {
            return false;
        }

        if (exception is BarReasonToWaitException)
        {
            // Special case, to not wrap. Will be handled infinitely in BarConnector.
            return false;
        }

        var report = exceptionArbiterService.GetReport(exception);

        if (report.AlreadyHandled && exception is BarException)
        {
            return false;
        }

        if (!report.IsExpected)
        {
            return false;
        }

        wrappedException = new BarException(exception)
        {
            CouldBeTransient = report.CouldBeTransient,
            IsHandled = true,
            CouldBeExternallySolvable = report.CouldBeExternallySolvable
        };
        return true;
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetRetryPipeline().ExecuteAsync(
                async token => await func(token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (TryGetWrappedException(exception, out var wrappedException) && wrappedException is not null)
            {
                throw wrappedException;
            }

            throw;
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
    {
        try
        {
            await GetRetryPipeline().ExecuteAsync(
                async token => await func(token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (TryGetWrappedException(exception, out var wrappedException) && wrappedException is not null)
            {
                throw wrappedException;
            }

            throw;
        }
    }

    internal sealed class ConfigurationModel
    {
        public required int? RetryCount { get; init; }

        public int EffectiveRetryCount => Math.Max(0, RetryCount ?? DefaultRetryCount);
    }
}