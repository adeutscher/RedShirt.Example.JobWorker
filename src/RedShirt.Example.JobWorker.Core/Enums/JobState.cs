namespace RedShirt.Example.JobWorker.Core.Enums;

/// <summary>
///     Lifecycle state of a job while it is tracked in the in-memory job repository.
/// </summary>
internal enum JobState
{
    /// <summary>
    ///     The job has been loaded into the repository and is waiting to be claimed by an executor.
    /// </summary>
    Inactive,

    /// <summary>
    ///     The job has been claimed by an executor and is currently being acted on.
    /// </summary>
    Active,

    /// <summary>
    ///     The executor could not acquire the idempotency lock because another instance is already working the same
    ///     idempotency key. Follow-up is deferred to the idempotency monitor.
    /// </summary>
    BlockedByIdempotency,

    /// <summary>
    ///     Processing for this repository entry by the executor has finished.
    ///     Heartbeats should stop, and the entry is eligible for removal.
    ///     To confirm, Complete in JobState is not meant to imply anything about how the execution
    ///     of the job or its acknowledgement went. It only means that the executor has finished and nothing more.
    /// </summary>
    Complete
}