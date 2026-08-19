namespace RedShirt.Example.JobWorker.Core.Enums;

internal enum HandlerComponentResponse
{
    /// <summary>
    ///     Indicates that a handler component ran to completion.
    ///     A Finished return type from one handler component implies that the other components will also be closing down
    ///     momentarily.
    /// </summary>
    Finished,

    /// <summary>
    ///     Indicates that a handler component was not enabled.
    ///     The Handler will not treat this component finishing as a harbinger of the others finishing.
    /// </summary>
    NotEnabled
}