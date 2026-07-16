using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Factories;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services;

public class AzureServiceBusJobSourceTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task TestGetJobsAsync(int batchSize)
    {
        var data1 = Guid.NewGuid().ToString();
        var mock1 = new Mock<IJobDataModel>().Object;
        var data2 = Guid.NewGuid().ToString();
        var mock2 = new Mock<IJobDataModel>().Object;

        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var bodyRetriever = new Mock<IAzureServiceBusBodyStringRetriever>();

        var message1 = new Mock<IServiceBusMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message1.Object))
            .Returns(data1);
        var message2 = new Mock<IServiceBusMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message2.Object))
            .Returns(data2);
        var message3 = new Mock<IServiceBusMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message3.Object))
            .Returns(data3);
        var message4 = new Mock<IServiceBusMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message4.Object))
            .Returns(data4);

        var azureMessageSource = new Mock<IAzureServiceBusMessageSource>(MockBehavior.Strict);
        azureMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                message1.Object,
                message2.Object,
                message3.Object,
                message4.Object
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

        var jobSource = new AzureServiceBusJobSource(source.Object, azureMessageSource.Object, converter.Object,
            bodyRetriever.Object,
            new NullLogger<AzureServiceBusJobSource>(), Options.Create(new AzureServiceBusConfigurationModel
            {
                VisibilityTimeoutSeconds = 0,
                MaxMessagesPerRequest = 0
            }));

        using var cts = new CancellationTokenSource();
        var response = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        client.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        azureMessageSource.Verify(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()), Times.Once);

        bodyRetriever.Verify(r => r.GetBody(It.IsAny<IServiceBusMessageContainer>()), Times.Exactly(4));
        bodyRetriever.Verify(r => r.GetBody(message1.Object), Times.Once);
        bodyRetriever.Verify(r => r.GetBody(message2.Object), Times.Once);
        bodyRetriever.Verify(r => r.GetBody(message3.Object), Times.Once);
        bodyRetriever.Verify(r => r.GetBody(message4.Object), Times.Once);

        converter.Verify(c => c.Convert(data1), Times.Once);
        converter.Verify(c => c.Convert(data2), Times.Once);
        converter.Verify(c => c.Convert(data3), Times.Once);
        converter.Verify(c => c.Convert(data4), Times.Once);

        Assert.Same(mock1, response.Items[0].Data);
        Assert.Same(mock2, response.Items[1].Data);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task TestGetJobsAsync_FailedToGetBody(int batchSize)
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var bodyRetriever = new Mock<IAzureServiceBusBodyStringRetriever>();

        var message1 = new Mock<IServiceBusMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message1.Object))
            .Returns(() => throw new Exception("Controlled Test Blast"));

        var azureMessageSource = new Mock<IAzureServiceBusMessageSource>(MockBehavior.Strict);
        azureMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                message1.Object
            ]);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);

        var jobSource = new AzureServiceBusJobSource(source.Object, azureMessageSource.Object, converter.Object,
            bodyRetriever.Object,
            new NullLogger<AzureServiceBusJobSource>(), Options.Create(new AzureServiceBusConfigurationModel
            {
                VisibilityTimeoutSeconds = 0,
                MaxMessagesPerRequest = 0
            }));

        using var cts = new CancellationTokenSource();
        var response = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Empty(response.Items);

        client.Verify(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        azureMessageSource.Verify(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()), Times.Once);

        bodyRetriever.Verify(r => r.GetBody(It.IsAny<IServiceBusMessageContainer>()), Times.Once);
        bodyRetriever.Verify(r => r.GetBody(message1.Object), Times.Once);

        converter.Verify(c => c.Convert(It.IsAny<string>()), Times.Never);

        source.Verify(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(
            c => c.DeadLetterMessageAsync(message1.Object, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(
            c => c.DeadLetterMessageAsync(message1.Object, It.IsAny<string>(), It.IsAny<string>(),
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var options = new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 20,
            MaxMessagesPerRequest = 0
        };

        var jobSource = new AzureServiceBusJobSource(null!, null!, null!, null!,
            new NullLogger<AzureServiceBusJobSource>(), Options.Create(options));

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Fail()
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 0,
            MaxMessagesPerRequest = 0
        };

        var jobSource = new AzureServiceBusJobSource(source.Object, null!, null!, null!,
            new NullLogger<AzureServiceBusJobSource>(),
            Options.Create(config));

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = null!
        };

        await jobSource.AcknowledgeCompletionAsync(job, false, TestContext.Current.CancellationToken);

        client.Verify(
            s => s.CompleteMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(s => s.CompleteMessageAsync(innerMessage.Object, TestContext.Current.CancellationToken),
            Times.Never);

        client.Verify(
            s => s.AbandonMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(s => s.AbandonMessageAsync(innerMessage.Object, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Test_AcknowledgeAsync_OffModel(bool success)
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 0,
            MaxMessagesPerRequest = 10
        };

        var jobSource = new AzureServiceBusJobSource(source.Object, null!, null!, null!,
            new NullLogger<AzureServiceBusJobSource>(),
            Options.Create(config));

        var job = new Mock<IJobModel>();

        await jobSource.AcknowledgeCompletionAsync(job.Object, success, TestContext.Current.CancellationToken);

        client.Verify(
            s => s.CompleteMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Success()
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 0,
            MaxMessagesPerRequest = 0
        };

        var jobSource = new AzureServiceBusJobSource(source.Object, null!, null!, null!,
            new NullLogger<AzureServiceBusJobSource>(),
            Options.Create(config));

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = null!
        };

        await jobSource.AcknowledgeCompletionAsync(job, true, TestContext.Current.CancellationToken);

        client.Verify(
            s => s.CompleteMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(s => s.CompleteMessageAsync(innerMessage.Object, TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    public async Task Test_HeartbeatAsync(int timeoutSeconds)
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = timeoutSeconds,
            MaxMessagesPerRequest = 0
        };

        var jobSource = new AzureServiceBusJobSource(source.Object, null!, null!, null!,
            new NullLogger<AzureServiceBusJobSource>(),
            Options.Create(config));

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = null!
        };

        await jobSource.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        client.Verify(
            c => c.RenewMessageLockAsync(It.IsAny<IServiceBusMessageContainer>(),
                It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(
            c => c.RenewMessageLockAsync(innerMessage.Object,
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync_OffModel()
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var source = new Mock<IBusReceiverClientSource>();
        source
            .Setup(s => s.GetQueueClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var config = new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 0,
            MaxMessagesPerRequest = 0
        };

        var jobSource = new AzureServiceBusJobSource(source.Object, null!, null!, null!,
            new NullLogger<AzureServiceBusJobSource>(),
            Options.Create(config));

        var job = new Mock<IJobModel>();

        await jobSource.HeartbeatAsync(job.Object, TestContext.Current.CancellationToken);

        client.Verify(
            c => c.RenewMessageLockAsync(It.IsAny<IServiceBusMessageContainer>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }
}