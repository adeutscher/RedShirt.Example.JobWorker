using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs;

internal interface IJobIntakeService
{
    Task SubmitAsync(IJobSourceResponse jobSourceResponse, CancellationToken cancellationToken);
}

internal sealed class JobIntakeService(
    IJobRepository jobRepository,
    ISourceMessageConverter sourceMessageConverter,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    IIdempotencyExecutionService idempotencyExecutionService,
    ICoreStatisticsService coreStatisticsService,
    ILogger<JobIntakeService> logger) : IJobIntakeService
{
    /// <summary>
    ///     Attempt to convert a raw message into job data.
    ///     Body retrieval is assumed to be reliably consistent; an exception from <see cref="IRawJobModel.Body" />
    ///     is treated as <see cref="CoreJobResult.InvalidData" />.
    /// </summary>
    private CoreJobResult TryConvert(IRawJobModel input, out IJobDataModel? convertedData, out Exception? exception)
    {
        convertedData = null;
        exception = null;

        // Body retrieval is assumed to be reliably consistent across reads of the same message.
        // Still, let us just retrieve it once.
        string? body;
        try
        {
            body = input.Body;
        }
        catch (Exception e)
        {
            exception = e;
            logger.LogError(e, "Error while retrieving job body");
            return CoreJobResult.InvalidData;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return CoreJobResult.Empty;
        }

        try
        {
            convertedData = sourceMessageConverter.Convert(body);

            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (convertedData is null)
            {
                return CoreJobResult.Parsing;
            }

            return CoreJobResult.Success;
        }
        catch (Exception e)
        {
            exception = e;
            logger.LogWarning(e, "Error while converting job intake");
            return CoreJobResult.Parsing;
        }
    }

    public async Task SubmitAsync(IJobSourceResponse jobSourceResponse, CancellationToken cancellationToken)
    {
        var convertedMessages = new List<IJobEnvelope>();
        var failedMessages = new List<FailedJobEnvelope>();

        foreach (var rawMessage in jobSourceResponse.Items)
        {
            coreStatisticsService.RecordReceived();

            var convertResult = TryConvert(rawMessage, out var convertedData, out var exception);
            if (convertResult == CoreJobResult.Success && convertedData is not null)
            {
                convertedMessages.Add(new JobEnvelope
                {
                    JobModel = new JobModel
                    {
                        MessageId = rawMessage.MessageId,
                        IdempotencyId = rawMessage.IdempotencyId,
                        CreatedAtUtc = rawMessage.CreatedAtUtc,
                        Data = convertedData
                    },
                    RawJobModel = rawMessage
                });
            }
            else
            {
                failedMessages.Add(new FailedJobEnvelope
                {
                    Result = convertResult,
                    Exception = exception,
                    RawJobModel = rawMessage
                });
            }
        }

        foreach (var failedMessage in failedMessages)
        {
            coreStatisticsService.RecordResult(failedMessage.Result);

            var acknowledgementResult = await safeJobAcknowledgementService.AcknowledgeSafelyAsync(
                failedMessage.RawJobModel,
                failedMessage.Result,
                failedMessage.Exception,
                // No previous attempt for this conversion attempt
                null,
                cancellationToken);
            await idempotencyExecutionService.SetResultInCacheAsync(
                failedMessage.RawJobModel,
                failedMessage.Result,
                acknowledgementResult,
                cancellationToken);
        }

        if (convertedMessages.Count > 0)
        {
            await jobRepository.LoadAsync(convertedMessages, cancellationToken);
        }
    }

    private sealed class FailedJobEnvelope
    {
        public required CoreJobResult Result { get; init; }
        public required Exception? Exception { get; init; }
        public required IRawJobModel RawJobModel { get; init; }
    }
}