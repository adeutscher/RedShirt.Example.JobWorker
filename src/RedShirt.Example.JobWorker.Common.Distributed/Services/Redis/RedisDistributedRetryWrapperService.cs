using Polly;
using RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services;

/// <summary>
///     Retries Distributed client operations that fail with expected transient exceptions,
///     then surfaces remaining failures as <see cref="WorkerDistributedException" />.
/// </summary>
public interface IDistributedRetryWrapperService
{
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for expected transient Distributed failures.
    /// </summary>
    /// <typeparam name="T">The result type produced by <paramref name="func" />.</typeparam>
    /// <param name="func">
    ///     The operation to execute. Receives the same <see cref="CancellationToken" /> used for retries and backoff delays.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation, retry attempts, and backoff delays.
    /// </param>
    /// <returns>The successful result of <paramref name="func" />.</returns>
    /// <exception cref="WorkerDistributedException">
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerDistributedException.IsTransient" />
    ///     reflects the arbiter judgement for the final exception.
    /// </exception>
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Executes <paramref name="func" /> with retry for expected transient Distributed failures.
    /// </summary>
    /// <param name="func">
    ///     The operation to execute. Receives the same <see cref="CancellationToken" /> used for retries and backoff delays.
    /// </param>
    /// <param name="cancellationToken">
    ///     Token used to cancel the operation, retry attempts, and backoff delays.
    /// </param>
    /// <returns>The successful result of <paramref name="func" />.</returns>
    /// <exception cref="WorkerDistributedException">
    ///     Thrown when <paramref name="func" /> ultimately fails. <see cref="WorkerDistributedException.IsTransient" />
    ///     reflects the arbiter judgement for the final exception.
    /// </exception>
    Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default);
}

public class RedisDistributedRetryWrapperService : IDistributedRetryWrapperService
{
    private const int RedisRetryCount = 3;

    /// <summary>
    ///     Lazily built Polly v8 <see cref="ResiliencePipeline" /> shared across invocations.
    /// </summary>
    private ResiliencePipeline? _retryPipeline;
    
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}