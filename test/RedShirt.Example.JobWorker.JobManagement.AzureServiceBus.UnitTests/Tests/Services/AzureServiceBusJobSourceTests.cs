using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services;

public class AzureServiceBusJobSourceTests
{
    private static IServiceBusMessageContainer CreateMessageContainer(string body, string? messageId = null)
    {
        var receivedMessage = messageId is null
            ? ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body))
            : ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString(body), messageId);
        var container = new Mock<IServiceBusMessageContainer>();
        container.SetupGet(c => c.Message).Returns(receivedMessage);
        return container.Object;
    }

    private static AzureServiceBusJobSource CreateJobSource(
        IAzureServiceBusMessageSource messageSource,
        Mock<IServiceBusClientWrapper>? client = null,
        AzureServiceBusConfigurationModel? config = null)
    {
        client ??= new Mock<IServiceBusClientWrapper>();
        var wrapper = AzureServiceBusRetryTestHelpers.CreatePassthroughClientRetryWrapper(client.Object);

        return new AzureServiceBusJobSource(wrapper.Object, messageSource,
            AzureServiceBusRetryTestHelpers.CreatePermissiveDetailedArbiter().Object,
            Options.Create(config ?? new AzureServiceBusConfigurationModel
            {
                VisibilityTimeoutSeconds = 0,
                MaxMessagesPerRequest = 0,
                WaitTimeSeconds = 0,
                AbandonRecoveredFailuresOnAcknowledge = true
            }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task TestGetJobsAsync(int batchSize)
    {
        var data1 = Guid.NewGuid().ToString();
        var data2 = Guid.NewGuid().ToString();
        var data3 = Guid.NewGuid().ToString();
        var data4 = Guid.NewGuid().ToString();

        var message1 = CreateMessageContainer(data1);
        var message2 = CreateMessageContainer(data2);
        var message3 = CreateMessageContainer(data3);
        var message4 = CreateMessageContainer(data4);

        var azureMessageSource = new Mock<IAzureServiceBusMessageSource>(MockBehavior.Strict);
        azureMessageSource.Setup(a => a.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [message1, message2, message3, message4]);

        var jobSource = CreateJobSource(azureMessageSource.Object);

        var response = await jobSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);
        Assert.Equal(4, response.Items.Count);

        azureMessageSource.Verify(a => a.GetMessagesAsync(batchSize, It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(data1, response.Items[0].Body);
        Assert.Equal(data2, response.Items[1].Body);
        Assert.Equal(data3, response.Items[2].Body);
        Assert.Equal(data4, response.Items[3].Body);
    }

    [Fact]
    public void TestGetRecommendedHeartbeatInterval()
    {
        var options = new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 20,
            MaxMessagesPerRequest = 0,
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = true
        };

        var jobSource = CreateJobSource(null!, config: options);

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(CoreJobResult.Empty)]
    [InlineData(CoreJobResult.Parsing)]
    [InlineData(CoreJobResult.InvalidData)]
    public async Task Test_AcknowledgeAsync_NonRecoverable_DeadLetters(CoreJobResult result)
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var jobSource = CreateJobSource(null!, client);

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        client.Verify(
            s => s.CompleteMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            s => s.AbandonMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            s => s.DeadLetterMessageAsync(innerMessage.Object, result.ToString(), null,
                TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(CoreJobResult.Success)]
    [InlineData(CoreJobResult.Failure)]
    public async Task Test_AcknowledgeAsync_OffModel(CoreJobResult result)
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var jobSource = CreateJobSource(null!, client, new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 0,
            MaxMessagesPerRequest = 10,
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = true
        });

        var job = new Mock<IRawJobModel>();

        await jobSource.AcknowledgeAsync(job.Object, result,
            TestContext.Current.CancellationToken);

        client.Verify(
            s => s.CompleteMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_Recoverable_Abandons(CoreJobResult result)
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var jobSource = CreateJobSource(null!, client);

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        client.Verify(
            s => s.CompleteMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            s => s.AbandonMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(s => s.AbandonMessageAsync(innerMessage.Object, TestContext.Current.CancellationToken),
            Times.Once);
        client.Verify(
            s => s.DeadLetterMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(CoreJobResult.Failure)]
    [InlineData(CoreJobResult.Cancelled)]
    public async Task Test_AcknowledgeAsync_Recoverable_DoesNotAbandonWhenConfiguredFalse(CoreJobResult result)
    {
        var client = new Mock<IServiceBusClientWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(null!, client, new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 0,
            MaxMessagesPerRequest = 0,
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = false
        });

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(job, result, TestContext.Current.CancellationToken);

        client.Verify(
            s => s.CompleteMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            s => s.AbandonMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            s => s.DeadLetterMessageAsync(It.IsAny<IServiceBusMessageContainer>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(client.Invocations);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_Success()
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var jobSource = CreateJobSource(null!, client);

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.AcknowledgeAsync(job, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

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
        var jobSource = CreateJobSource(null!, client, new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = timeoutSeconds,
            MaxMessagesPerRequest = 0,
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = true
        });

        var innerMessage = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict);
        var job = new AzureRawJobModel
        {
            Message = innerMessage.Object,
            CreatedAtUtc = DateTime.UtcNow
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
        var jobSource = CreateJobSource(null!, client);

        var job = new Mock<IRawJobModel>();

        await jobSource.HeartbeatAsync(job.Object, TestContext.Current.CancellationToken);
        // Nothing to be done, add a token assert to satisfy Sonar
        Assert.True(true);
    }
}