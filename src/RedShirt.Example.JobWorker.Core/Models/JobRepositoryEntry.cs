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

    /// <summary>
    ///     Current processing state of this job.
    ///     May not be set to <c>null</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the setter is given <c>null</c>.</exception>
    JobState? State { get; set; }

    void SubscribeToStateChange(Action<JobState> action);
}

internal sealed class JobRepositoryEntry : IJobRepositoryEntry
{
    /// <summary>
    ///     Thread-safety for mutable field access from maintainer, executor, and repository threads.
    /// </summary>
    private readonly Lock _lock = new();

    private Action<JobState>? _stateChangeCallbacks;

    public required IRawJobModel RawJobModel { get; init; }
    public required IJobModel JobModel { get; init; }

    public bool CanHeartbeat
    {
        get
        {
            lock (_lock)
            {
                return field;
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
                field = false;
            }
        }
    } = true;

    public required DateTime LastHeartbeatTime
    {
        get
        {
            lock (_lock)
            {
                return field;
            }
        }
        set
        {
            lock (_lock)
            {
                field = value;
            }
        }
    }

    public required JobState? State
    {
        get
        {
            lock (_lock)
            {
                return field;
            }
        }
        set
        {
            if (value is not { } newState)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Action<JobState>? callbacks;
            lock (_lock)
            {
                if (field == newState)
                {
                    return;
                }

                field = newState;
                callbacks = _stateChangeCallbacks;
            }

            callbacks?.Invoke(newState);
        }
    } = JobState.Inactive;

    public void SubscribeToStateChange(Action<JobState> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_lock)
        {
            _stateChangeCallbacks += action;
        }
    }
}