using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.UnitTests;

internal static class TestJobHelpers
{
    public static Mock<IRawJobModel> CreateRawJobModel(string? messageId = null, string? idempotencyId = null)
    {
        var raw = new Mock<IRawJobModel>(MockBehavior.Strict);
        raw.Setup(r => r.MessageId).Returns(messageId ?? Guid.NewGuid().ToString());
        raw.Setup(r => r.IdempotencyId).Returns(idempotencyId);
        raw.Setup(r => r.Body).Returns("{}");
        raw.Setup(r => r.CreatedAtUtc).Returns(DateTime.UtcNow);
        return raw;
    }

    public static Mock<IJobModel> CreateJobModel(string? messageId = null, string? idempotencyId = null)
    {
        var job = new Mock<IJobModel>(MockBehavior.Strict);
        job.Setup(j => j.MessageId).Returns(messageId ?? Guid.NewGuid().ToString());
        job.Setup(j => j.IdempotencyId).Returns(idempotencyId);
        job.Setup(j => j.CreatedAtUtc).Returns(DateTime.UtcNow);
        job.Setup(j => j.Data).Returns(new Mock<IJobDataModel>().Object);
        return job;
    }

    public static JobEnvelope CreateEnvelope(IJobModel jobModel, IRawJobModel rawJobModel)
    {
        return new JobEnvelope
        {
            JobModel = jobModel,
            RawJobModel = rawJobModel
        };
    }

    public static JobEnvelope CreateEnvelope(Mock<IJobModel> jobModel, Mock<IRawJobModel>? rawJobModel = null)
    {
        return CreateEnvelope(jobModel.Object, (rawJobModel ?? CreateRawJobModel(jobModel.Object.MessageId)).Object);
    }

    public static IReadOnlyList<IJobEnvelope> EnvelopesFromJobModels(params IJobModel[] jobModels)
    {
        return jobModels
            .Select(jm => CreateEnvelope(jm, CreateRawJobModel(jm.MessageId).Object))
            .ToList();
    }

    public static JobSourceResponse CreateJobSourceResponse(params IRawJobModel[] items)
    {
        return new JobSourceResponse
        {
            Items = items.ToList()
        };
    }

    public static SafeAcknowledgementResult AckResult(bool acknowledgedSuccessfully, bool? loggedFailureSuccessfully = null)
    {
        return new SafeAcknowledgementResult
        {
            AcknowledgedSuccessfully = acknowledgedSuccessfully,
            LoggedFailureSuccessfully = loggedFailureSuccessfully
        };
    }

    public static IdempotencyCacheResult CacheResult(bool jobSuccess, bool acknowledgedSuccessfully = true,
        bool? loggedFailureSuccessfully = null)
    {
        return new IdempotencyCacheResult
        {
            JobSuccess = jobSuccess,
            AcknowledgementResult = AckResult(acknowledgedSuccessfully, loggedFailureSuccessfully)
        };
    }
}
