using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class HighLevelStreamSourceTests
{
    [Fact]
    public async Task Test_AcknowledgeAsync()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);

        var @lock = new Mock<IAbstractedLock>();
        @lock.Setup(l => l.IsAcquired)
            .Returns(true);
        using var cts = new CancellationTokenSource();

        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object,
            lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>())
        {
            Lock = @lock.Object,
            JobCount = 2,
            JobCountTally = 0
        };

        await streamSource.AcknowledgeCompletionAsync(null!, false, TestContext.Current.CancellationToken);
        Assert.Equal(1, streamSource.JobCountTally);
        @lock.Verify(l => l.IsAcquired, Times.Once);
        @lock.Verify(l => l.Unlock(), Times.Never);

        await streamSource.AcknowledgeCompletionAsync(null!, false, TestContext.Current.CancellationToken);
        Assert.Equal(2, streamSource.JobCountTally);
        @lock.Verify(l => l.IsAcquired, Times.Exactly(2));
        @lock.Verify(l => l.Unlock(), Times.Once);
        Assert.Null(streamSource.Lock);
    }

    [Fact]
    public async Task Test_AcknowledgeAsync_NullLock()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);

        using var cts = new CancellationTokenSource();

        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object,
            lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>());

        await streamSource.AcknowledgeCompletionAsync(null!, false, TestContext.Current.CancellationToken);

        // Nothing really to verify here other than not throwing an exception
        // Asserting true to satisfy SonarQube
        Assert.True(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Test_GetJobsAsync(int batchSize)
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);

        var @lock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        @lock.Setup(l => l.IsAcquired)
            .Returns(true);

        var jobModel = new Mock<IJobModel>().Object;

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["foo"]);

        locker.Setup(l => l.GetLockAsync(It.IsAny<string>(), TestContext.Current.CancellationToken))
            .ReturnsAsync(@lock.Object);

        checkpointStorage.Setup(c => c.GetCheckpointAsync("foo", TestContext.Current.CancellationToken))
            .ReturnsAsync("bar");

        lowLevelStreamSource.Setup(l => l.GetJobsAsync(batchSize, "bar", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "1",
                LastSequenceNumber = "2",
                Items =
                [
                    jobModel
                ]
            });

        checkpointStorage.Setup(c => c.UpdateShortTermAsync("foo", "1", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        checkpointStorage.Setup(c => c.UpdateLongTermAsync("foo", "2", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object,
            lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>());

        var response = await streamSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Equal(0, streamSource.RecommendedHeartbeatIntervalSeconds);
        var item = Assert.Single(response.Items);
        Assert.Same(jobModel, item);

        Assert.Single(locker.Invocations);
        locker.Verify(l => l.GetLockAsync("foo", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(1, streamSource.JobCount);
        Assert.NotNull(streamSource.Lock);

        Assert.Single(checkpointStorage.Invocations);
        checkpointStorage.Verify(cs => cs.GetCheckpointAsync("foo", TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_GetJobsAsync_CouldNotGetLock()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);

        using var cts = new CancellationTokenSource();

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["foo", "bar"]);

        locker.Setup(l => l.GetLockAsync(It.IsAny<string>(), TestContext.Current.CancellationToken))
            .ReturnsAsync(new Mock<IAbstractedLock>().Object);

        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object,
            lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>());

        var response = await streamSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        locker.Verify(l => l.GetLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        Assert.Equal(0, streamSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Test_GetJobsAsync_InvalidOperation_Twice()
    {
        var streamSource =
            new HighLevelStreamSource(null!, null!, null!, null!, new NullLogger<HighLevelStreamSource>())
            {
                Lock = new Mock<IAbstractedLock>().Object
            };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await streamSource.GetJobsAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Test_GetJobsAsync_NoJobs()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);

        var @lock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        @lock.Setup(l => l.IsAcquired)
            .Returns(true);
        @lock.Setup(l => l.Unlock());

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["foo"]);

        locker.Setup(l => l.GetLockAsync(It.IsAny<string>(), TestContext.Current.CancellationToken))
            .ReturnsAsync(@lock.Object);

        checkpointStorage.Setup(c => c.GetCheckpointAsync("foo", TestContext.Current.CancellationToken))
            .ReturnsAsync("bar");

        lowLevelStreamSource.Setup(l => l.GetJobsAsync(1, "bar", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "1",
                LastSequenceNumber = "2",
                Items = []
            });

        checkpointStorage.Setup(c => c.UpdateShortTermAsync("foo", "1", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        checkpointStorage.Setup(c => c.UpdateLongTermAsync("foo", "2", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object,
            lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>());

        var response = await streamSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(0, streamSource.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(response.Items);

        Assert.Single(locker.Invocations);
        locker.Verify(l => l.GetLockAsync("foo", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(0, streamSource.JobCount);
        Assert.Null(streamSource.Lock);

        Assert.Equal(3, checkpointStorage.Invocations.Count);
        checkpointStorage.Verify(cs => cs.GetCheckpointAsync("foo", TestContext.Current.CancellationToken),
            Times.Once);
        checkpointStorage.Verify(c => c.UpdateShortTermAsync("foo", "1", TestContext.Current.CancellationToken),
            Times.Once);
        checkpointStorage.Verify(c => c.UpdateLongTermAsync("foo", "2", TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task Test_HeartbeatAsync()
    {
        var streamSource = new HighLevelStreamSource(null!, null!, null!,
            null!, new NullLogger<HighLevelStreamSource>());

        await streamSource.HeartbeatAsync(null!, TestContext.Current.CancellationToken);
        Assert.Null(streamSource.Lock);
    }

    [Fact]
    public async Task Test_MoveTracker()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>();
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);

        var response = new StreamSourceResponse
        {
            IteratorString = "A",
            LastSequenceNumber = "B",
            Items = null!
        };

        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object,
            lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>())
        {
            LastShard = "SHARD",
            LastStreamSourceResponse = response
        };

        await streamSource.MoveTrackerAsync(TestContext.Current.CancellationToken);

        Assert.Empty(lowLevelStreamSource.Invocations);
        Assert.Empty(lister.Invocations);
        Assert.Empty(locker.Invocations);

        Assert.Equal(2, checkpointStorage.Invocations.Count);
        checkpointStorage.Verify(c => c.UpdateShortTermAsync("SHARD", "A", TestContext.Current.CancellationToken),
            Times.Once);
        checkpointStorage.Verify(c => c.UpdateLongTermAsync("SHARD", "B", TestContext.Current.CancellationToken),
            Times.Once);
    }
}