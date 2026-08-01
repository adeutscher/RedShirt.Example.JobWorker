using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.Idempotency;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Safety;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs;

public class JobIntakeServiceTests
{
    private static IJobSourceResponse CreateJobSourceResponse(List<IRawJobModel> items)
    {
        var response = new Mock<IJobSourceResponse>(MockBehavior.Strict);
        response.Setup(r => r.Items).Returns(items);
        return response.Object;
    }

    [Fact]
    public async Task SubmitAsync_WhenNoItems_DoesNotLoadOrAcknowledge()
    {
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        var acknowledgement = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        var idempotency = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);

        var service = new JobIntakeService(
            jobRepository.Object,
            converter.Object,
            acknowledgement.Object,
            idempotency.Object,
            new NullLogger<JobIntakeService>());

        await service.SubmitAsync(CreateJobSourceResponse([]), TestContext.Current.CancellationToken);

        jobRepository.Verify(
            r => r.LoadAsync(It.IsAny<IReadOnlyList<IJobEnvelope>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        acknowledgement.Verify(
            a => a.AcknowledgeSafelyAsync(It.IsAny<IRawJobModel>(), It.IsAny<bool>(), It.IsAny<Exception?>(),
                It.IsAny<SafeAcknowledgementResult?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotency.Verify(
            i => i.SetResultInCacheAsync(It.IsAny<IRawJobModel>(), It.IsAny<bool>(),
                It.IsAny<ISafeAcknowledgementResult>(), It.IsAny<CancellationToken>()),
            Times.Never);
        converter.Verify(c => c.Convert(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenBodyConverts_LoadsEnvelopeAndSkipsFailureHandling()
    {
        var createdAt = new DateTime(2024, 7, 8, 9, 10, 11, DateTimeKind.Utc);
        var rawJob = new Mock<IRawJobModel>(MockBehavior.Strict);
        rawJob.Setup(r => r.MessageId).Returns("msg-1");
        rawJob.Setup(r => r.IdempotencyId).Returns("idem-1");
        rawJob.Setup(r => r.CreatedAtUtc).Returns(createdAt);
        rawJob.Setup(r => r.Body).Returns("""{"foo":"bar"}""");

        var jobData = new Mock<IJobDataModel>(MockBehavior.Strict);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert("""{"foo":"bar"}""")).Returns(jobData.Object);

        IReadOnlyList<IJobEnvelope>? loaded = null;
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<IReadOnlyList<IJobEnvelope>>(), TestContext.Current.CancellationToken))
            .Callback<IReadOnlyList<IJobEnvelope>, CancellationToken>((items, _) => loaded = items)
            .Returns(Task.CompletedTask);

        var acknowledgement = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        var idempotency = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);

        var service = new JobIntakeService(
            jobRepository.Object,
            converter.Object,
            acknowledgement.Object,
            idempotency.Object,
            new NullLogger<JobIntakeService>());

        await service.SubmitAsync(
            CreateJobSourceResponse([rawJob.Object]),
            TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        Assert.Same(rawJob.Object, loaded[0].RawJobModel);
        Assert.IsType<JobModel>(loaded[0].JobModel);
        Assert.Equal("msg-1", loaded[0].JobModel.MessageId);
        Assert.Equal("idem-1", loaded[0].JobModel.IdempotencyId);
        Assert.Equal(createdAt, loaded[0].JobModel.CreatedAtUtc);
        Assert.Same(jobData.Object, loaded[0].JobModel.Data);

        acknowledgement.Verify(
            a => a.AcknowledgeSafelyAsync(It.IsAny<IRawJobModel>(), It.IsAny<bool>(), It.IsAny<Exception?>(),
                It.IsAny<SafeAcknowledgementResult?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotency.Verify(
            i => i.SetResultInCacheAsync(It.IsAny<IRawJobModel>(), It.IsAny<bool>(),
                It.IsAny<ISafeAcknowledgementResult>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenBodyIsEmpty_AcknowledgesFailureWithoutCallingConverter()
    {
        var rawJob = new Mock<IRawJobModel>(MockBehavior.Strict);
        rawJob.Setup(r => r.Body).Returns("   ");

        var ackResult = new SafeAcknowledgementResult
        {
            LoggedFailureSuccessfully = true,
            AcknowledgedSuccessfully = true
        };

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var acknowledgement = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        acknowledgement
            .Setup(a => a.AcknowledgeSafelyAsync(rawJob.Object, false, null, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(ackResult);

        var idempotency = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotency
            .Setup(i => i.SetResultInCacheAsync(rawJob.Object, false, ackResult, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var service = new JobIntakeService(
            jobRepository.Object,
            converter.Object,
            acknowledgement.Object,
            idempotency.Object,
            new NullLogger<JobIntakeService>());

        await service.SubmitAsync(
            CreateJobSourceResponse([rawJob.Object]),
            TestContext.Current.CancellationToken);

        converter.Verify(c => c.Convert(It.IsAny<string>()), Times.Never);
        jobRepository.Verify(
            r => r.LoadAsync(It.IsAny<IReadOnlyList<IJobEnvelope>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        acknowledgement.Verify(
            a => a.AcknowledgeSafelyAsync(rawJob.Object, false, null, null, TestContext.Current.CancellationToken),
            Times.Once);
        idempotency.Verify(
            i => i.SetResultInCacheAsync(rawJob.Object, false, ackResult, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenConverterThrows_AcknowledgesFailureWithException()
    {
        var rawJob = new Mock<IRawJobModel>(MockBehavior.Strict);
        rawJob.Setup(r => r.Body).Returns("not-json");

        var conversionException = new InvalidOperationException("parse failed");

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert("not-json")).Throws(conversionException);

        var ackResult = new SafeAcknowledgementResult
        {
            LoggedFailureSuccessfully = false,
            AcknowledgedSuccessfully = true
        };

        var acknowledgement = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        acknowledgement
            .Setup(a => a.AcknowledgeSafelyAsync(rawJob.Object, false, conversionException, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(ackResult);

        var idempotency = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotency
            .Setup(i => i.SetResultInCacheAsync(rawJob.Object, false, ackResult, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var service = new JobIntakeService(
            jobRepository.Object,
            converter.Object,
            acknowledgement.Object,
            idempotency.Object,
            new NullLogger<JobIntakeService>());

        await service.SubmitAsync(
            CreateJobSourceResponse([rawJob.Object]),
            TestContext.Current.CancellationToken);

        acknowledgement.Verify(
            a => a.AcknowledgeSafelyAsync(rawJob.Object, false, conversionException, null,
                TestContext.Current.CancellationToken),
            Times.Once);
        idempotency.Verify(
            i => i.SetResultInCacheAsync(rawJob.Object, false, ackResult, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(
            r => r.LoadAsync(It.IsAny<IReadOnlyList<IJobEnvelope>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenConverterReturnsNull_AcknowledgesFailureWithoutException()
    {
        var rawJob = new Mock<IRawJobModel>(MockBehavior.Strict);
        rawJob.Setup(r => r.Body).Returns("{}");

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert("{}")).Returns((IJobDataModel?) null);

        var ackResult = new SafeAcknowledgementResult
        {
            LoggedFailureSuccessfully = true,
            AcknowledgedSuccessfully = false
        };

        var acknowledgement = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        acknowledgement
            .Setup(a => a.AcknowledgeSafelyAsync(rawJob.Object, false, null, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(ackResult);

        var idempotency = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotency
            .Setup(i => i.SetResultInCacheAsync(rawJob.Object, false, ackResult, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);

        var service = new JobIntakeService(
            jobRepository.Object,
            converter.Object,
            acknowledgement.Object,
            idempotency.Object,
            new NullLogger<JobIntakeService>());

        await service.SubmitAsync(
            CreateJobSourceResponse([rawJob.Object]),
            TestContext.Current.CancellationToken);

        acknowledgement.Verify(
            a => a.AcknowledgeSafelyAsync(rawJob.Object, false, null, null, TestContext.Current.CancellationToken),
            Times.Once);
        jobRepository.Verify(
            r => r.LoadAsync(It.IsAny<IReadOnlyList<IJobEnvelope>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenMixedSuccessAndFailure_LoadsOnlyConvertedAndAcknowledgesFailures()
    {
        var goodRaw = new Mock<IRawJobModel>(MockBehavior.Strict);
        goodRaw.Setup(r => r.MessageId).Returns("good");
        goodRaw.Setup(r => r.IdempotencyId).Returns((string?) null);
        goodRaw.Setup(r => r.CreatedAtUtc).Returns(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        goodRaw.Setup(r => r.Body).Returns("good-body");

        var badRaw = new Mock<IRawJobModel>(MockBehavior.Strict);
        badRaw.Setup(r => r.Body).Returns("bad-body");

        var jobData = new Mock<IJobDataModel>(MockBehavior.Strict);
        var conversionException = new FormatException("nope");

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert("good-body")).Returns(jobData.Object);
        converter.Setup(c => c.Convert("bad-body")).Throws(conversionException);

        var ackResult = new SafeAcknowledgementResult
        {
            LoggedFailureSuccessfully = true,
            AcknowledgedSuccessfully = true
        };

        var acknowledgement = new Mock<ISafeJobAcknowledgementService>(MockBehavior.Strict);
        acknowledgement
            .Setup(a => a.AcknowledgeSafelyAsync(badRaw.Object, false, conversionException, null,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(ackResult);

        var idempotency = new Mock<IIdempotencyExecutionService>(MockBehavior.Strict);
        idempotency
            .Setup(i => i.SetResultInCacheAsync(badRaw.Object, false, ackResult, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        IReadOnlyList<IJobEnvelope>? loaded = null;
        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.LoadAsync(It.IsAny<IReadOnlyList<IJobEnvelope>>(), TestContext.Current.CancellationToken))
            .Callback<IReadOnlyList<IJobEnvelope>, CancellationToken>((items, _) => loaded = items)
            .Returns(Task.CompletedTask);

        var service = new JobIntakeService(
            jobRepository.Object,
            converter.Object,
            acknowledgement.Object,
            idempotency.Object,
            new NullLogger<JobIntakeService>());

        await service.SubmitAsync(
            CreateJobSourceResponse([goodRaw.Object, badRaw.Object]),
            TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        Assert.Same(goodRaw.Object, loaded[0].RawJobModel);
        Assert.Same(jobData.Object, loaded[0].JobModel.Data);

        acknowledgement.Verify(
            a => a.AcknowledgeSafelyAsync(badRaw.Object, false, conversionException, null,
                TestContext.Current.CancellationToken),
            Times.Once);
        acknowledgement.Verify(
            a => a.AcknowledgeSafelyAsync(goodRaw.Object, It.IsAny<bool>(), It.IsAny<Exception?>(),
                It.IsAny<SafeAcknowledgementResult?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        idempotency.Verify(
            i => i.SetResultInCacheAsync(badRaw.Object, false, ackResult, TestContext.Current.CancellationToken),
            Times.Once);
    }
}
