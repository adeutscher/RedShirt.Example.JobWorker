using RedShirt.Example.JobWorker.Core.Exceptions.MessagePolling;

namespace RedShirt.Example.JobWorker.Core.Exceptions;

/// <summary>
///     Used internally by the worker loop when the job source does not produce any messages.
/// </summary>
internal sealed class NoJobException : ReasonToWaitException;