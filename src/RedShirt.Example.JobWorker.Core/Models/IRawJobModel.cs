namespace RedShirt.Example.JobWorker.Core.Models;

public interface IRawJobModel
{
    string MessageId { get; }
    string? IdempotencyId { get; }

    /// <summary>
    ///     Message payload.
    ///     Body retrieval is assumed to be reliably consistent: repeated reads of the same message should
    ///     yield the same result or throw the same class of failure. An exception from this getter is treated
    ///     as an unrecoverable <c>InvalidData</c> outcome at intake.
    /// </summary>
    string? Body { get; }

    DateTime CreatedAtUtc { get; }
}