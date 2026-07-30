namespace RedShirt.Example.JobWorker.Core.Services.MessagePolling;

/// <summary>
///     The loader is responsible for loading messages from the job source into the in-memory job repository for job
///     executors to handle.
/// </summary>
internal interface IJobLoader
{
    Task RunAsync(CancellationToken cancellationToken = default);
}