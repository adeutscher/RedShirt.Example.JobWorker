using RedShirt.Example.JobWorker.Core.Services.ExecutionState;

namespace RedShirt.Example.JobWorker.Core.Services.Health;

/// <summary>
///     Ready when the loader has started, has not finished, and shutdown has not been signaled.
/// </summary>
internal sealed class WorkerReadiness(
    IJobLoaderStateReaderService loaderState,
    IExecutionEndArbiter endArbiter) : IWorkerReadiness
{
    public bool IsReady()
    {
        if (!endArbiter.ShouldKeepRunning())
        {
            return false;
        }

        if (!loaderState.HasLoaderStarted())
        {
            return false;
        }

        if (loaderState.IsLoaderFinished())
        {
            return false;
        }

        return true;
    }
}
