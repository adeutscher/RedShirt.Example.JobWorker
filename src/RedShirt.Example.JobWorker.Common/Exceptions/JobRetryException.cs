namespace RedShirt.Example.JobWorker.Common.Exceptions;

/// <summary>
///     An exception that can be thrown from within a job to trigger an internal retry.
/// </summary>
public sealed class JobRetryException : Exception
{
    /// <summary>
    ///     Time that Core should wait before re-executing a job.
    /// </summary>
    public int DelayTimeMilliseconds { get; init; }
}