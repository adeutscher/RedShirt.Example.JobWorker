using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Configuration;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Services;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Services;

public class AzureServiceBusSubscribeJobSourceTests
{
    private static AzureServiceBusSubscribeJobSource CreateJobSource(
        Mock<IServiceBusClientWrapper>? client = null,
        AzureServiceBusConfigurationModel? config = null)
    {
        client ??= new Mock<IServiceBusClientWrapper>();
        var clientRetryWrapper =
            AzureServiceBusRetryTestHelpers.CreatePassthroughClientRetryWrapper(client.Object);

        return new AzureServiceBusSubscribeJobSource(
            clientRetryWrapper.Object,
            AzureServiceBusRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            null!,
            null!,
            null!,
            null!,
            AzureServiceBusRetryTestHelpers.CreatePermissiveDetailedArbiter().Object,
            Options.Create(config ?? new AzureServiceBusConfigurationModel
            {
                VisibilityTimeoutSeconds = 20,
                MaxMessagesPerRequest = 0,
                WaitTimeSeconds = 0,
                AbandonRecoveredFailuresOnAcknowledge = true
            }),
            NullLogger<AzureServiceBusSubscribeJobSource>.Instance);
    }

    [Fact]
    public void RecommendedHeartbeatIntervalSeconds_MatchesPollJobSourceFormula()
    {
        var jobSource = CreateJobSource(config: new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 20,
            MaxMessagesPerRequest = 0,
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = true
        });

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    public async Task HeartbeatAsync_RenewsMessageLock(int timeoutSeconds)
    {
        var client = new Mock<IServiceBusClientWrapper>();
        var jobSource = CreateJobSource(client, new AzureServiceBusConfigurationModel
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
    public async Task HeartbeatAsync_OffModel_DoesNothing()
    {
        var client = new Mock<IServiceBusClientWrapper>(MockBehavior.Strict);
        var jobSource = CreateJobSource(client);

        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);

        Assert.Empty(client.Invocations);
    }
}
