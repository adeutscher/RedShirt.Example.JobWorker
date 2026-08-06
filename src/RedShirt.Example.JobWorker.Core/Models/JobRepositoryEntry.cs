using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Models;

internal interface ISortableJobWrapper
{
    IJobModel JobModel { get; }
}

internal interface IJobRepositoryEntry : ISortableJobWrapper
{
    IRawJobModel RawJobModel { get; }
    bool CanHeartbeat { get; }
    DateTime LastHeartbeatTime { get; }
    JobState State { get; }

    Task SetAsCannotHeartbeatAsync(CancellationToken cancellationToken = default);
    Task SetLastHeartbeatTimeAsync(DateTime lastHeartbeatTime, CancellationToken cancellationToken = default);
    Task SetStateAsync(JobState state, CancellationToken cancellationToken = default);
}

internal sealed class JobRepositoryEntry : IJobRepositoryEntry
{
    /// <summary>
    ///     Thread-safety for mutable field access from maintainer, executor, and repository threads.
    /// </summary>
    private readonly Lock _lock = new();

    private bool _canHeartbeat = true;
    private DateTime _lastHeartbeatTime;
    private JobState _state = JobState.Inactive;

    public required IRawJobModel RawJobModel { get; init; }
    public required IJobModel JobModel { get; init; }

    public bool CanHeartbeat
    {
        get
        {
            lock (_lock)
            {
                return _canHeartbeat;
            }
        }
    }

    public DateTime LastHeartbeatTime
    {
        get
        {
            lock (_lock)
            {
                return _lastHeartbeatTime;
            }
        }
    }

    public JobState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public Task SetAsCannotHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _canHeartbeat = false;
        }

        return Task.CompletedTask;
    }

    public Task SetLastHeartbeatTimeAsync(DateTime lastHeartbeatTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _lastHeartbeatTime = lastHeartbeatTime;
        }

        return Task.CompletedTask;
    }

    public Task SetStateAsync(JobState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _state = state;
        }

        return Task.CompletedTask;
    }
}