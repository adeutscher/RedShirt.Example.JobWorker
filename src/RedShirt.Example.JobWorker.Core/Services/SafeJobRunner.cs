using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Ensures a safe execution of a job with no leaked exceptions.
/// </summary>
internal interface ISafeJobRunner
{
    Task<bool> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default);
}

/// <summary>
///     Try/Catch layer around the execution of a job.
/// </summary>
/// <param name="jobLogicRunner"></param>
/// <param name="jobFailureHandler"></param>
/// <param name="logger"></param>
/// <param name="options"></param>
internal class SafeJobRunner(
    IJobLogicRunner jobLogicRunner,
    IJobFailureHandler jobFailureHandler,
    ILogger<SafeJobRunner> logger,
    IOptions<SafeJobRunner.ConfigurationModel> options) : ISafeJobRunner
{
    public async Task<bool> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        try
        {
            var safeJobModel = new SafeJobModel
            {
                MessageId = job.MessageId,
                IdempotencyId = job.IdempotencyId,
                CreatedAtUtc = job.CreatedAtUtc,
                Data = job.Data
            };

            await Policy.Handle<JobRetryException>()
                .RetryAsync(
                    Math.Max(0, options.Value.InternalRetryCount),
                    async (e, _) =>
                    {
                        if (e is JobRetryException {DelayTimeMilliseconds: > 0} retryException)
                        {
                            // User has requested that the job handler wait for a certain amount of time before retrying.
                            await Task.Delay(TimeSpan.FromMilliseconds(retryException.DelayTimeMilliseconds),
                                cancellationToken);
                        }
                    }
                )
                .ExecuteAsync(() => jobLogicRunner.RunAsync(safeJobModel, cancellationToken));
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error running job: {EMessage}", e.Message);

            try
            {
                await jobFailureHandler.HandleFailureAsync(job, e, cancellationToken);
            }
            catch (Exception e2)
            {
                logger.LogError(e2, "Job failure handling failed: {EMessage}", e2.Message);
            }

            return false;
        }
    }

    public sealed class ConfigurationModel
    {
        public required int InternalRetryCount { get; init; }
    }

    /// <summary>
    ///     Basic implementation of IJobModel
    ///     Information from the upstream copy of IJobModel is presented to the user.
    ///     This is meant as a just-in-case to protect the original IJobModel against any and all shenanigans.
    ///     The IJobModel fields on an implementation may be implemented as simple get/inits,
    ///     but many job source implementations of IJobModel also store library-specific constructs on them
    ///     that someone *could* mess with in theory.
    ///     It would be very unusual for a developer to kneecap their own application in this way,
    ///     and so I don't really know why I'm adding this layer of discouragement.
    ///     But here we are, adding a silly little just-in-case layer.
    /// </summary>
    private sealed class SafeJobModel : IJobModel
    {
        public required string MessageId { get; init; }
        public required string? IdempotencyId { get; init; }
        public required DateTime CreatedAtUtc { get; init; }
        public required IJobDataModel Data { get; init; }
    }
}