namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

/// <summary>
///     The implementations of IJobLoader are responsible for running a loop iteration within JobLoaderLoop.
/// </summary>
internal interface IJobLoader
{
    Task RunAsync(CancellationToken cancellationToken = default);
}