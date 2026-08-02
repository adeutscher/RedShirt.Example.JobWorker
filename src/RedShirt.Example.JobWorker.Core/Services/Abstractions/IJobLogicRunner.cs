using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Abstractions;

/// <summary>
///     BatchHandler of actual job logic. Used by Core.Logic project to implement consumer-specific logic.
/// </summary>
public interface IJobLogicRunner
{
    /// <summary>
    ///     Execute application work for a parsed job.
    ///     Return <see cref="JobResult.Success" /> on completion, or <see cref="JobResult.Broken" /> when the job
    ///     is identified as unrecoverable without throwing.
    ///     Unexpected exceptions (and exhausted <see cref="JobRetryException" /> retries) are treated by Core as a
    ///     recoverable <see cref="CoreJobResult.Failure" />; a returned <see cref="JobResult.Broken" /> maps to
    ///     <see cref="CoreJobResult.Broken" />.
    /// </summary>
    /// <param name="job">Parsed job model ready for business logic.</param>
    /// <param name="cancellationToken">Token used to cancel in-flight work.</param>
    /// <returns>
    ///     <see cref="JobResult.Success" /> or <see cref="JobResult.Broken" /> describing the logical outcome.
    /// </returns>
    Task<JobResult> RunAsync(IJobModel job, CancellationToken cancellationToken = default);
}