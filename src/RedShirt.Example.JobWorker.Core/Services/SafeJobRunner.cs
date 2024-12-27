using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services;

internal interface ISafeJobRunner
{
    Task<bool> RunSafelyAsync(IJobModel job, CancellationToken cancellationToken = default);
}

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
            await Policy.Handle<JobRetryException>()
                .RetryAsync(Math.Max(0, options.Value.InternalRetryCount))
                .ExecuteAsync(() => jobLogicRunner.RunAsync(job.Data, cancellationToken));
            return true;
        }
        catch (Exception e)
        {
            logger.LogError("Error running job: {EMessage}", e.Message);

            try
            {
                await jobFailureHandler.HandleFailureAsync(job, e, cancellationToken);
            }
            catch (Exception e2)
            {
                logger.LogError(e2, "Job failure handling failed");
            }

            return false;
        }
    }

    public sealed class ConfigurationModel
    {
        public required int InternalRetryCount { get; init; }
    }
}