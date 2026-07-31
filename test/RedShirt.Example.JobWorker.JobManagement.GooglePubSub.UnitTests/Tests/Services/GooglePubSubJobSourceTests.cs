using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Configuration;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Factories;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class GooglePubSubJobSourceTests
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

        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var bodyRetriever = new Mock<IGooglePubSubBodyStringRetriever>();

        var message1 = new Mock<IPubSubMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message1.Object)).Returns(data1);
        var message2 = new Mock<IPubSubMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message2.Object)).Returns(data2);
        var message3 = new Mock<IPubSubMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message3.Object)).Returns(data3);
        var message4 = new Mock<IPubSubMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message4.Object)).Returns(data4);

        var pubSubMessageSource = new Mock<IGooglePubSubMessageSource>(MockBehavior.Strict);
        pubSubMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message1.Object, message2.Object, message3.Object, message4.Object]);

        var converter = new Mock<ISourceMessageConverter>(MockBehavior.Strict);
        converter.Setup(c => c.Convert(data1)).Returns(mock1);
        converter.Setup(c => c.Convert(data2)).Returns(mock2);
        converter.Setup(c => c.Convert(data3)).Returns((IJobDataModel?) null);
        converter.Setup(c => c.Convert(data4)).Returns((string _) => throw new Exception());

        var jobSource = new GooglePubSubJobSource(source.Object, pubSubMessageSource.Object, converter.Object,
            bodyRetriever.Object,
            new NullLogger<GooglePubSubJobSource>(), Options.Create(new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                VisibilityTimeoutSeconds = 60,
                MaxMessagesPerRequest = 100
            }));

        var response = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(2, response.Items.Count);

        pubSubMessageSource.Verify(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.AcknowledgeAsync(message3.Object, It.IsAny<CancellationToken>()), Times.Once);

        Assert.Same(mock1, response.Items[0].Data);
        Assert.Same(mock2, response.Items[1].Data);
    }

    [Fact]
    public async Task TestGetJobsAsync_FailedToGetBody_AcknowledgesPoison()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var bodyRetriever = new Mock<IGooglePubSubBodyStringRetriever>();
        var message1 = new Mock<IPubSubMessageContainer>();
        bodyRetriever.Setup(m => m.GetBody(message1.Object))
            .Returns(() => throw new Exception("Controlled Test Blast"));

        var pubSubMessageSource = new Mock<IGooglePubSubMessageSource>(MockBehavior.Strict);
        pubSubMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message1.Object]);

        var jobSource = new GooglePubSubJobSource(source.Object, pubSubMessageSource.Object,
            new Mock<ISourceMessageConverter>().Object,
            bodyRetriever.Object,
            new NullLogger<GooglePubSubJobSource>(), Options.Create(new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                VisibilityTimeoutSeconds = 60,
                MaxMessagesPerRequest = 100
            }));

        var response = await jobSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        client.Verify(c => c.AcknowledgeAsync(message1.Object, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestAcknowledgeCompletionAsync(bool success)
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var message = new Mock<IPubSubMessageContainer>();
        var job = new GooglePubSubJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            new Mock<ISourceMessageConverter>().Object,
            new Mock<IGooglePubSubBodyStringRetriever>().Object,
            new NullLogger<GooglePubSubJobSource>(), Options.Create(new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                VisibilityTimeoutSeconds = 60,
                MaxMessagesPerRequest = 100
            }));

        await jobSource.AcknowledgeCompletionAsync(job, success, TestContext.Current.CancellationToken);

        if (success)
        {
            client.Verify(c => c.AcknowledgeAsync(message.Object, It.IsAny<CancellationToken>()), Times.Once);
            client.Verify(c => c.NackAsync(It.IsAny<IPubSubMessageContainer>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        else
        {
            client.Verify(c => c.NackAsync(message.Object, It.IsAny<CancellationToken>()), Times.Once);
            client.Verify(c => c.AcknowledgeAsync(It.IsAny<IPubSubMessageContainer>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Fact]
    public async Task TestHeartbeatAsync()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var message = new Mock<IPubSubMessageContainer>();
        var job = new GooglePubSubJobModel
        {
            Message = message.Object,
            CreatedAtUtc = DateTime.UtcNow,
            Data = new Mock<IJobDataModel>().Object
        };

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            new Mock<ISourceMessageConverter>().Object,
            new Mock<IGooglePubSubBodyStringRetriever>().Object,
            new NullLogger<GooglePubSubJobSource>(), Options.Create(new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                VisibilityTimeoutSeconds = 60,
                MaxMessagesPerRequest = 100
            }));

        Assert.Equal(45, jobSource.RecommendedHeartbeatIntervalSeconds);

        await jobSource.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        client.Verify(c => c.ModifyAckDeadlineAsync(message.Object, 60, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeAndHeartbeat_IgnoreNonPubSubJobs()
    {
        var client = new Mock<IPubSubSubscriberClientWrapper>();
        var source = new Mock<IPubSubSubscriberClientSource>();
        source
            .Setup(s => s.GetSubscriberClientAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var jobSource = new GooglePubSubJobSource(source.Object, new Mock<IGooglePubSubMessageSource>().Object,
            new Mock<ISourceMessageConverter>().Object,
            new Mock<IGooglePubSubBodyStringRetriever>().Object,
            new NullLogger<GooglePubSubJobSource>(), Options.Create(new GooglePubSubConfigurationModel
            {
                ProjectId = "local-pubsub",
                SubscriptionId = "jobs-subscription",
                VisibilityTimeoutSeconds = 60,
                MaxMessagesPerRequest = 100
            }));

        await jobSource.AcknowledgeCompletionAsync(new Mock<IJobModel>().Object, true,
            TestContext.Current.CancellationToken);
        await jobSource.HeartbeatAsync(new Mock<IJobModel>().Object, TestContext.Current.CancellationToken);

        client.Verify(c => c.AcknowledgeAsync(It.IsAny<IPubSubMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            c => c.ModifyAckDeadlineAsync(It.IsAny<IPubSubMessageContainer>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }
}
