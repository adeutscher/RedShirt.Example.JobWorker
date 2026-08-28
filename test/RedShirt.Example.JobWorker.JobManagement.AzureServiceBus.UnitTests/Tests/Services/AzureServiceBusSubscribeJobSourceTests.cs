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
        AzureServiceBusConfigurationModel? config = null)
    {
        var clientRetryWrapper =
            AzureServiceBusRetryTestHelpers.CreatePassthroughClientRetryWrapper(new Mock<IServiceBusClientWrapper>()
                .Object);

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
    public async Task HeartbeatAsync_OffModel_DoesNothing()
    {
        var jobSource = CreateJobSource();

        await jobSource.HeartbeatAsync(new Mock<IRawJobModel>().Object, TestContext.Current.CancellationToken);

        Assert.True(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    public async Task HeartbeatAsync_RenewsMessageLock(int timeoutSeconds)
    {
        var jobSource = CreateJobSource(new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = timeoutSeconds,
            MaxMessagesPerRequest = 0,
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = true
        });

        var lockExtender = new Mock<IServiceBusMessageLockExtender>(MockBehavior.Strict);
        lockExtender
            .Setup(h => h.RenewMessageLockAsync(TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var job = new AzureRawJobModel
        {
            Message = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict).Object,
            LockExtender = lockExtender.Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        lockExtender.Verify(h => h.RenewMessageLockAsync(TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task HeartbeatAsync_WithoutLockExtender_DoesNothing()
    {
        var jobSource = CreateJobSource();
        var job = new AzureRawJobModel
        {
            Message = new Mock<IServiceBusMessageContainer>(MockBehavior.Strict).Object,
            CreatedAtUtc = DateTime.UtcNow
        };

        await jobSource.HeartbeatAsync(job, TestContext.Current.CancellationToken);

        Assert.True(true);
    }

    [Fact]
    public void RecommendedHeartbeatIntervalSeconds_MatchesPollJobSourceFormula()
    {
        var jobSource = CreateJobSource(new AzureServiceBusConfigurationModel
        {
            VisibilityTimeoutSeconds = 20,
            MaxMessagesPerRequest = 0,
            WaitTimeSeconds = 0,
            AbandonRecoveredFailuresOnAcknowledge = true
        });

        Assert.Equal(15, jobSource.RecommendedHeartbeatIntervalSeconds);
    }
}