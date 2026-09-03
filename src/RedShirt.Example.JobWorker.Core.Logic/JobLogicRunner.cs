using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Services;

namespace RedShirt.Example.JobWorker.Core.Logic;

internal sealed class JobLogicRunner(
    IBarConnector barConnector,
    ISleepService sleepService,
    IOptions<JobLogicRunner.ConfigurationModel> options,
    ILogger<JobLogicRunner> logger) : IJobLogicRunner
{
    public async Task<IJobLogicRunnerResponse> RunAsync(IJobModel job, CancellationToken cancellationToken = default)
    {
        var requestedSleepSeconds = job.Data.SleepDurationSeconds;

        if (!options.Value.EffectiveAccessBarEnabled)
        {
            // Bar access is not enabled, just do standard sleep
            logger.LogInformation("Sleeping for {DurationSeconds} seconds", requestedSleepSeconds);
            await sleepService.DelayAsync(TimeSpan.FromSeconds(requestedSleepSeconds), cancellationToken);

            return new JobLogicRunnerResponse
            {
                Result = JobResult.Success
            };
        }

        // Bar access is enabled

        // A value of 404 or 429 suggests a special
        var effectiveSleepSeconds = requestedSleepSeconds is 404 or 429 ? 1 : requestedSleepSeconds;
        logger.LogInformation("Sleeping for {DurationSeconds} seconds before accessing Bar connector",
            effectiveSleepSeconds);
        await sleepService.DelayAsync(TimeSpan.FromSeconds(effectiveSleepSeconds), cancellationToken);

        var barId = Math.Max(1, requestedSleepSeconds);
        var barRecord = await barConnector.GetByIdAsync(barId, cancellationToken);
        logger.LogInformation("Bar record {BarId} resolved to {BarName}", barRecord.Id, barRecord.Name);

        return new JobLogicRunnerResponse
        {
            Result = JobResult.Success
        };
    }

    internal sealed class ConfigurationModel
    {
        public string? AccessBarEnabled { get; init; }

        /// <summary>
        ///     Parsing of <see cref="AccessBarEnabled" />. Values greater than zero or bool-parsed <c>true</c> are treated as
        ///     enabled.
        /// </summary>
        public bool EffectiveAccessBarEnabled => !string.IsNullOrWhiteSpace(AccessBarEnabled)
                                                 && (
                                                     (int.TryParse(AccessBarEnabled, out var intResult)
                                                      && intResult > 0)
                                                     || (bool.TryParse(AccessBarEnabled, out var boolResult)
                                                         && boolResult)
                                                 );
    }
}