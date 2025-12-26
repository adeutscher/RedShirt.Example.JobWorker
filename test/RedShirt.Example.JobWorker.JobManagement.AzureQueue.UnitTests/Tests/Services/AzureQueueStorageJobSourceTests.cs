using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureQueue.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureQueue.UnitTests.Tests.Services;

public class AzureQueueStorageJobSourceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Test_AcknowledgeAsync(bool success)
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClient())
            .Returns(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 0
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!, null!,
            new NullLogger<AzureQueueStorageJobSource>(),
            Options.Create(config));

        var innerMessage = new Mock<IQueueMessageModel>(MockBehavior.Strict);
        var job = new AzureJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = null!
        };

        await jobSource.AcknowledgeCompletionAsync(job, success, TestContext.Current.CancellationToken);

        client.Verify(s => s.DeleteMessageAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(s => s.DeleteMessageAsync(innerMessage.Object, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Test_AcknowledgeAsync_OffModel(bool success)
    {
        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClient())
            .Returns(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 0
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!, null!,
            new NullLogger<AzureQueueStorageJobSource>(),
            Options.Create(config));

        var job = new Mock<IJobModel>();

        await jobSource.AcknowledgeCompletionAsync(job.Object, success, TestContext.Current.CancellationToken);

        client.Verify(s => s.DeleteMessageAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
            .Setup(s => s.GetQueueClient())
            .Returns(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = timeoutSeconds
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!, null!,
            new NullLogger<AzureQueueStorageJobSource>(),
            Options.Create(config));

        var innerMessage = new Mock<IQueueMessageModel>(MockBehavior.Strict);
        var job = new AzureJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = null!
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
            .Setup(s => s.GetQueueClient())
            .Returns(client.Object);

        var config = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = timeoutSeconds
        };

        var jobSource = new AzureQueueStorageJobSource(source.Object, null!, null!,
            new NullLogger<AzureQueueStorageJobSource>(),
            Options.Create(config));

        var job = new Mock<IJobModel>();

        await jobSource.HeartbeatAsync(job.Object, TestContext.Current.CancellationToken);

        client.Verify(
            c => c.SetMessageVisibilityTimeoutAsync(It.IsAny<IQueueMessageModel>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task TestGetJobsAsync(int batchSize)
    {
        var receiptHandle1 = Guid.NewGuid().ToString();
        var data1 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var receiptHandle2 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var mock2 = new Mock<IJobDataModel>().Object;

        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var client = new Mock<IQueueConsumerClientWrapper>();
        var source = new Mock<IQueueConsumerClientSource>();
        source
            .Setup(s => s.GetQueueClient())
            .Returns(client.Object);

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

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1))
            .Returns(mock1);
        converter.Setup(c => c.Convert(data2))
            .Returns(mock2);
        converter.Setup(c => c.Convert(data3))
            .Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data4))
            .Returns((string _) => throw new Exception());

        const int visibilityTimeoutInSeconds = 100;

        var jobSource = new AzureQueueStorageJobSource(source.Object, azureMessageSource.Object, converter.Object,
            new NullLogger<AzureQueueStorageJobSource>(), Options.Create(new AzureQueueStorageConfigurationModel
            {
                VisibilityTimeoutSeconds = visibilityTimeoutInSeconds
            }));

        using var cts = new CancellationTokenSource();
        var response = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        client.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        azureMessageSource.Verify(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()), Times.Once);

        converter.Verify(c => c.Convert(data1), Times.Once);
        converter.Verify(c => c.Convert(data2), Times.Once);
        converter.Verify(c => c.Convert(data3), Times.Once);
        converter.Verify(c => c.Convert(data4), Times.Once);

        Assert.Equal(receiptHandle1, response.Items[0].MessageId);
        Assert.Same(mock1, response.Items[0].Data);
        Assert.Equal(receiptHandle2, response.Items[1].MessageId);
        Assert.Same(mock2, response.Items[1].Data);
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var options = new AzureQueueStorageConfigurationModel
        {
            VisibilityTimeoutSeconds = 20
        };

        var jobSource = new AzureQueueStorageJobSource(null!, null!, null!,
            new NullLogger<AzureQueueStorageJobSource>(), Options.Create(options));

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    private sealed class BasicMessageModel : IQueueMessageModel
    {
        public required string Body { get; init; }
        public required string MessageId { get; init; }
        public required string PopReceipt { get; init; }
    }
}