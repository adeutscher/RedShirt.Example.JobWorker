using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs;

internal interface IJobIntakeService
{
    Task SubmitAsync(IJobSourceResponse jobSourceResponse, CancellationToken cancellationToken);
}

internal class JobIntakeService(
    IJobRepository jobRepository,
    ISourceMessageConverter sourceMessageConverter,
    ISafeJobAcknowledgementService safeJobAcknowledgementService,
    IIdempotencyExecutionService idempotencyExecutionService,
    ILogger<JobIntakeService> logger) : IJobIntakeService
{
    private bool ConvertData(IRawJobModel input, out IJobDataModel? convertedData, out Exception? exception)
    {
        convertedData = null;
        exception = null;

        try
        {
            var body = input.Body;

            if (string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            convertedData = sourceMessageConverter.Convert(body);
            return true;
        }
        catch (Exception e)
        {
            exception = e;
            logger.LogError(e, "Error while converting job intake");
            return false;
        }
    }

    public async Task SubmitAsync(IJobSourceResponse jobSourceResponse, CancellationToken cancellationToken)
    {
        var convertedMessages = new List<IJobEnvelope>();
        var failedMessages = new List<FailedJobEnvelope>();

        foreach (var rawMessage in jobSourceResponse.Items)
        {
            if (ConvertData(rawMessage, out var convertedData, out var exception) && convertedData is not null)
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
                    Exception = exception,
                    RawJobModel = rawMessage
                });
            }
        }

        foreach (var failedMessage in failedMessages)
        {
            var acknowledgementResult = await safeJobAcknowledgementService.AcknowledgeSafelyAsync(
                failedMessage.RawJobModel,
                false,
                failedMessage.Exception,
                // No previous attempt for this conversion attempt
                null,
                cancellationToken);
            await idempotencyExecutionService.SetResultInCacheAsync(
                failedMessage.RawJobModel,
                false,
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
        public required Exception? Exception { get; init; }
        public required IRawJobModel RawJobModel { get; init; }
    }
}