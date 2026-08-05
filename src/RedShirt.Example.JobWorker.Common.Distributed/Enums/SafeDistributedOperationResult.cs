namespace RedShirt.Example.JobWorker.Common.Distributed.Enums;

public enum SafeDistributedOperationResult
{
    /// <summary>
    ///     The safe distributed attempt was successful.
    ///     To confirm phrasing, this does not imply anything about the success of the overall operation.
    ///     For example, attempting to get cached data will be interpreted as a <see cref="Success" /> even if there is no
    ///     cached data to be found.
    ///     It only means that the server didn't time out or suffer a similar transient failure.
    /// </summary>
    Success,

    /// <summary>
    ///     The safe distributed attempt service has attempted the distributed operation, but the attempt failed in an expected
    ///     manner.
    ///     After returning Failure, the safe distributed operation service shall enter a "Disgrace" state for a configured
    ///     amount of time. During this time attempting safe distributed operations shall immediately return a
    ///     <see cref="DisgracePeriod" /> instead of attempting the actual service.
    /// </summary>
    Failure,

    /// <summary>
    ///     The safe distributed operation service is currently in a "Disgrace" state after a previous attempt failed.
    ///     The intent of the "Disgrace" period is to minimize timeout operations that are just "nice-to-have" as opposed to
    ///     mission-critical.
    ///     The safe distributed operation service shall become available again after a configured amount of time.
    ///     The operation is assumed to be unreachable during this disgrace period.
    /// </summary>
    DisgracePeriod
}