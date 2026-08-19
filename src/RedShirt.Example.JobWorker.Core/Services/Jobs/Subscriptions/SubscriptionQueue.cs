using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using RedShirt.Example.JobWorker.Core.Utility;
using System.Collections.Concurrent;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

public interface IJobSubscriberIntakeQueue
{
    Task<IJobSourceResponse?> GetNextAsync(CancellationToken cancellationToken = default);
    void Load(IJobSourceResponse jobSourceResponse);
}

internal class JobSubscriberIntakeQueue : IJobSubscriberIntakeQueue
{
    private readonly AsyncManualResetEvent _doNotWaitIfSetEvent = new();
    private readonly ConcurrentQueue<IJobSourceResponse> _jobs = new();
    private bool _done;

    private void Cancel()
    {
        _done = true;
        _doNotWaitIfSetEvent.Set();
    }

    public JobSubscriberIntakeQueue(IExecutionEndArbiter executionEndArbiter)
    {
        executionEndArbiter.AddOnStopCallback(_ => Cancel());
    }

    public void Load(IJobSourceResponse jobSourceResponse)
    {
        _jobs.Enqueue(jobSourceResponse);
        _doNotWaitIfSetEvent.Set();
    }

    public async Task<IJobSourceResponse?> GetNextAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _doNotWaitIfSetEvent.WaitAsync(cancellationToken);
            if (_jobs.TryDequeue(out var jobSourceResponse))
            {
                return jobSourceResponse;
            }

            _doNotWaitIfSetEvent.Reset();

            if (_done)
            {
                return null;
            }
        }
    }
}