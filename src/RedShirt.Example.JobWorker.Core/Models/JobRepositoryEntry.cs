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
}

internal sealed class JobRepositoryEntry : IJobRepositoryEntry
{
    /// <summary>
    ///     Thread-safety for mutable field access from executor, monitor, and repository threads.
    /// </summary>
    private readonly Lock _lock = new();

    private Action<IJobRepositoryEntry, JobState?, JobState>? _stateCallbacks;

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

            Action<IJobRepositoryEntry, JobState?, JobState>? callbacks;
            JobState? original;
            lock (_lock)
            {
                if (field == newState)
                {
                    return;
                }

                original = field;
                field = newState;
                callbacks = _stateCallbacks;
            }

            callbacks?.Invoke(this, original, newState);
        }
    } = JobState.Inactive;

    /// <summary>
    ///     Register a callback invoked with this entry plus the original and current <see cref="State" />.
    ///     Invoked immediately on subscribe when the current state is not <c>null</c>;
    ///     the original state is <c>null</c> for that first invocation.
    /// </summary>
    /// <param name="action">
    ///     Receives this entry, the original state (possibly <c>null</c>), and the current non-null state.
    /// </param>
    public void SubscribeToState(Action<IJobRepositoryEntry, JobState?, JobState> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        JobState? current;
        lock (_lock)
        {
            _stateCallbacks += action;
            current = State;
        }

        if (current is { } state)
        {
            action(this, null, state);
        }
    }
}