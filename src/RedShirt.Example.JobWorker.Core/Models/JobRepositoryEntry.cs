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

    /// <summary>
    ///     Whether this job can still receive heartbeats.
    ///     May only be set to <c>false</c>.
    /// </summary>
    bool CanHeartbeat { get; set; }

    DateTime LastHeartbeatTime { get; set; }
    JobState State { get; set; }

    void SubscribeToStateChange(Action<JobState> action);
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
    private Action<JobState>? _stateChangeCallbacks;

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
        set
        {
            if (value)
            {
                throw new ArgumentException("CanHeartbeat can only be set to false.", nameof(value));
            }

            lock (_lock)
            {
                _canHeartbeat = false;
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
        set
        {
            lock (_lock)
            {
                _lastHeartbeatTime = value;
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
        set
        {
            Action<JobState>? callbacks;
            lock (_lock)
            {
                if (_state == value)
                {
                    return;
                }

                _state = value;
                callbacks = _stateChangeCallbacks;
            }

            callbacks?.Invoke(value);
        }
    }

    public void SubscribeToStateChange(Action<JobState> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_lock)
        {
            _stateChangeCallbacks += action;
        }
    }
}