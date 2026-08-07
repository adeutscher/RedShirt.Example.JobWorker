using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Exceptions;
using RedShirt.Example.JobWorker.Common.Models;

namespace RedShirt.Example.JobWorker.Common.Services.Abstractions;

/// <summary>
///     BatchHandler of actual job logic. Used by Core.Logic project to implement consumer-specific logic.
/// </summary>
public interface IJobLogicRunner
{
    /// <summary>
    ///     Execute application work for a parsed job.
    ///     Return a response whose <see cref="IJobLogicRunnerResponse.Result" /> is
    ///     <see cref="JobResult.Success" /> on completion,
    ///     <see cref="JobResult.Failure" /> for a recoverable failure without throwing
    ///     (equivalent to an uncaught exception in application logic),
    ///     or <see cref="JobResult.InvalidData" /> when the job is identified as unrecoverable without throwing.
    ///     Unexpected exceptions (and exhausted <see cref="JobRetryException" /> retries) are treated by Core as a
    ///     recoverable failure; a returned <see cref="JobResult.Failure" /> maps the same way;
    ///     a returned <see cref="JobResult.InvalidData" /> also maps an invalid job. />.
    /// </summary>
    /// <param name="job">Parsed job model ready for business logic.</param>
    /// <param name="cancellationToken">Token used to cancel in-flight work.</param>
    /// <returns>
    ///     An <see cref="IJobLogicRunnerResponse" /> whose <see cref="IJobLogicRunnerResponse.Result" /> is
    ///     <see cref="JobResult.Success" />, <see cref="JobResult.Failure" />, or <see cref="JobResult.InvalidData" />
    ///     describing the logical outcome.
    /// </returns>
    Task<IJobLogicRunnerResponse> RunAsync(IJobModel job, CancellationToken cancellationToken = default);
}