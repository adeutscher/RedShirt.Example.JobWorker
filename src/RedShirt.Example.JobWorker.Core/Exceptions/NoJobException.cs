namespace RedShirt.Example.JobWorker.Core.Exceptions;

/// <summary>
///     Used internally by the worker loop when the job source does not produce any messages.
/// </summary>
public sealed class NoJobException : Exception;