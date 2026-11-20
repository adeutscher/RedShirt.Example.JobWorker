using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Services;

public class AzureQueueStorageJobSourceTests
{
    [Theory]
    [InlineData(null, AzureQueueStorageConfigurationModel.DefaultMaximumReceives)]
    [InlineData(0, AzureQueueStorageConfigurationModel.DefaultMaximumReceives)]
    [InlineData(-1, AzureQueueStorageConfigurationModel.DefaultMaximumReceives)]
    [InlineData(1, 1)]
    [InlineData(12, 12)]
    public void TestEffectiveMaximumReceives(int? configured, int expected)
    {
        Assert.Equal(expected, new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 0,
            MaximumReceives = configured
        }.EffectiveMaximumReceives);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task TestGetJobsAsync(int batchSize)
    {
        var receiptHandle1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var receiptHandle2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var azureMessageSource = new Mock<IAzureQueueStorageMessageSource>(MockBehavior.Strict);
        azureMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                new BasicMessageModel
                {
                    MessageId = receiptHandle1,
                    Body = data1,
                    PopReceipt = null!
                },
                new BasicMessageModel
                {
                    MessageId = receiptHandle2,
                    Body = data2,
                    PopReceipt = null!
                },
                new BasicMessageModel
                {
                    MessageId = Guid.NewGuid().ToString(), // moot
                    Body = data3,
                    PopReceipt = null!
                },
                new BasicMessageModel
                {
                    MessageId = Guid.NewGuid().ToString(), // moot
                    Body = data4,
                    PopReceipt = null!
                }
            ]);

        var jobSource = new AzureQueueStorageJobSource(source.Object, azureMessageSource.Object,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new AzureQueueStorageConfigurationModel
            {
                VisibilityTimeoutSeconds = 100
            }),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        using var cts = new CancellationTokenSource();
        var response = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(4, response.Items.Count);

        client.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        azureMessageSource.Verify(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(receiptHandle1, response.Items[0].MessageId);
        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(receiptHandle2, response.Items[1].MessageId);
        Assert.Equal(data2, response.Items[1].Body);
        Assert.Equal(data3, response.Items[2].Body);
        Assert.Equal(data4, response.Items[3].Body);
    }

    [Fact]
    public async Task TestGetJobsAsync_DiscardDeleteFailure_SkipsMessageAndKeepsBatch()
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var overLimit = new BasicMessageModel
            {MessageId = "over-limit", Body = "b", PopReceipt = "r2", DequeueCount = 9};
        var fresh = new BasicMessageModel {MessageId = "fresh", Body = "c", PopReceipt = "r3", DequeueCount = 1};
        client.Setup(s => s.DeleteMessageAsync(overLimit, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("delete failed"));

        var azureMessageSource = new Mock<IAzureQueueStorageMessageSource>(MockBehavior.Strict);
        azureMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [overLimit, fresh]);

        var jobSource = new AzureQueueStorageJobSource(source.Object, azureMessageSource.Object,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new AzureQueueStorageConfigurationModel
            {
                VisibilityTimeoutSeconds = 100
            }),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var response = await jobSource.GetJobsAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal("fresh", Assert.Single(response.Items).MessageId);
    }

    [Fact]
    public async Task TestGetJobsAsync_DiscardsMessagesReceivedMoreThanMaximum()
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var atLimit = new BasicMessageModel {MessageId = "at-limit", Body = "a", PopReceipt = "r1", DequeueCount = 3};
        var overLimit = new BasicMessageModel
            {MessageId = "over-limit", Body = "b", PopReceipt = "r2", DequeueCount = 4};
        var fresh = new BasicMessageModel {MessageId = "fresh", Body = "c", PopReceipt = "r3", DequeueCount = 1};

        var azureMessageSource = new Mock<IAzureQueueStorageMessageSource>(MockBehavior.Strict);
        azureMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [atLimit, overLimit, fresh]);

        var jobSource = new AzureQueueStorageJobSource(source.Object, azureMessageSource.Object,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new AzureQueueStorageConfigurationModel
            {
                VisibilityTimeoutSeconds = 100,
                MaximumReceives = 3
            }),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var response = await jobSource.GetJobsAsync(10, TestContext.Current.CancellationToken);

        Assert.Equal(["at-limit", "fresh"], response.Items.Select(i => i.MessageId));
        client.Verify(s => s.DeleteMessageAsync(overLimit, TestContext.Current.CancellationToken), Times.Once);
        client.Verify(s => s.DeleteMessageAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var options = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 20
        };

        var jobSource = new AzureQueueStorageJobSource(null!, null!,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(options),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.InvalidData)]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    public async Task Test_AcknowledgeAsync_Deletes(CoreJobResult result)
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 0
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(config),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var innerMessage = new Mock<IQueueMessageModel>(MockBehavior.Strict);
        var job = new AzureQueueStorageRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(job, result,
            TestContext.Current.CancellationToken);

        client.Verify(s => s.DeleteMessageAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(s => s.DeleteMessageAsync(innerMessage.Object, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    public async Task Test_AcknowledgeAsync_OffModel(CoreJobResult result)
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 0
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(config),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var job = new Mock<IRawJobModel>();

        await jobSource.AcknowledgeAsync(job.Object, result,
            TestContext.Current.CancellationToken);

        client.Verify(s => s.DeleteMessageAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    // Default limit of five receives.
    [InlineData(null, 4, false)]
    [InlineData(null, 5, true)]
    [InlineData(null, 9, true)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(1, 1, true)]
    public async Task Test_AcknowledgeAsync_Recoverable_DeletesOnLastReceive(int? maximumReceives, long dequeueCount,
        bool expectDelete)
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new AzureQueueStorageConfigurationModel
            {
                VisibilityTimeoutSeconds = 0,
                MaximumReceives = maximumReceives
            }),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var innerMessage = new BasicMessageModel
        {
            MessageId = "m",
            Body = "body",
            PopReceipt = "receipt",
            DequeueCount = dequeueCount
        };

        await jobSource.AcknowledgeAsync(new AzureQueueStorageRawJobModel
        {
            Message = innerMessage,
            CreatedAtUtc = DateTime.UtcNow
        }, CoreJobResult.Failure, TestContext.Current.CancellationToken);

        client.Verify(s => s.DeleteMessageAsync(innerMessage, TestContext.Current.CancellationToken),
            expectDelete ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_Recoverable_DoesNotDelete(CoreJobResult result)
    {
        var client = new Mock<IQueueConsumerClientWrapper>(MockBehavior.Strict);
        var source = new Mock<IQueueConsumerClientSource>(MockBehavior.Strict);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 0
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(config),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var innerMessage = new Mock<IQueueMessageModel>(MockBehavior.Strict);
        innerMessage.SetupGet(m => m.DequeueCount).Returns(1);
        var job = new AzureQueueStorageRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        Assert.Empty(source.Invocations);
        Assert.Empty(client.Invocations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    public async Task Test_HeartbeatAsync(int timeoutSeconds)
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = timeoutSeconds
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(config),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var innerMessage = new Mock<IQueueMessageModel>(MockBehavior.Strict);
        var job = new AzureQueueStorageRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        client.Verify(
            c => c.SetMessageVisibilityTimeoutAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(
            c => c.SetMessageVisibilityTimeoutAsync(innerMessage.Object, It.IsAny<TimeSpan>(),
                TestContext.Current.CancellationToken), Times.Once);
        client.Verify(
            c => c.SetMessageVisibilityTimeoutAsync(innerMessage.Object,
                It.Is<TimeSpan>(ts => ts.Seconds == config.EffectiveVisibilityTimeoutSeconds),
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync_OffModel()
    {
        var timeoutSeconds = 123; // moot

        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = timeoutSeconds
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!,
            AzureQueueStorageRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(config),
            NullLogger<AzureQueueStorageJobSource>.Instance);

        var job = new Mock<IRawJobModel>();

        await jobSource.HeartbeatAsync(job.Object, TestContext.Current.CancellationToken);

        client.Verify(
            c => c.SetMessageVisibilityTimeoutAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class BasicMessageModel : IQueueMessageModel
    {
        public required string Body { get; init; }
        public required string MessageId { get; init; }
        public required string PopReceipt { get; init; }
        public long DequeueCount { get; init; } = 1;
    }
}