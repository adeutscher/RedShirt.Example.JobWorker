namespace RedShirt.Example.JobWorker.Core.Enums;

internal enum HandlerComponentResponse
{
    /// <summary>
    ///     Indicates that a handler component threw an <see cref="OperationCanceledException" />.
    ///     A properly-implemented handler component should never return this.
    ///     An Exception return type is set within the handler as a fallback in lieu of a proper response from the component.
    /// </summary>
    Cancelled,

    /// <summary>
    ///     Indicates that a handler component threw an exception.
    ///     A properly-implemented handler component should never return this.
    ///     An Exception return type is set within the handler as a fallback in lieu of a proper response from the component.
    /// </summary>
    Exception,

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