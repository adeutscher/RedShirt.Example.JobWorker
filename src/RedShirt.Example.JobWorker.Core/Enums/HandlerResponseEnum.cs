namespace RedShirt.Example.JobWorker.Core.Enums;

public enum HandlerResponseEnum
{
    /// <summary>
    ///     Indicates that a handler component ran to completion.
    /// </summary>
    Finished,

    /// <summary>
    ///     Indicates that a handler component was not enabled.
    ///     The Handler will not treat this component finishing as noteworthy.
    /// </summary>
    NotEnabled
}