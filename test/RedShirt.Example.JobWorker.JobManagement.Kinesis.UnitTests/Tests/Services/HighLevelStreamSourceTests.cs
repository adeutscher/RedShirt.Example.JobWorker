using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Utility;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class HighLevelStreamSourceTests
{
    private static HighLevelStreamSource CreateStreamSource(
        Mock<ICheckpointStorage>? checkpointStorage = null,
        Mock<IKinesisShardLister>? lister = null,
        Mock<IAbstractedLockService>? locker = null,
        Mock<ILowLevelStreamSource>? lowLevelStreamSource = null)
    {
        return new HighLevelStreamSource(
            (checkpointStorage ?? new Mock<ICheckpointStorage>(MockBehavior.Strict)).Object,
            (lister ?? new Mock<IKinesisShardLister>(MockBehavior.Strict)).Object,
            (locker ?? new Mock<IAbstractedLockService>(MockBehavior.Strict)).Object,
            (lowLevelStreamSource ?? new Mock<ILowLevelStreamSource>(MockBehavior.Strict)).Object,
            new PassthroughRetryWrapper(),
            new NullLogger<HighLevelStreamSource>());
    }

    private static Mock<IAbstractedLock> CreateAcquiredLock()
    {
        var lockHandle = new Mock<IAbstractedLock>(MockBehavior.Strict);
        lockHandle.SetupGet(l => l.IsAcquired).Returns(true);
        // GetJobsAsync unlocks in finally whenever the session is not retained.
        lockHandle.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return lockHandle;
    }

    private static Mock<IAbstractedLock> CreateUnacquiredLock()
    {
        var lockHandle = new Mock<IAbstractedLock>(MockBehavior.Strict);
        lockHandle.SetupGet(l => l.IsAcquired).Returns(false);
        // Even unacquired locks are unlocked in GetJobsAsync's finally when no session is stored.
        lockHandle.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return lockHandle;
    }

    private static KinesisJobModel CreateKinesisJob(string shardId, string? messageId = null)
    {
        return new KinesisJobModel
        {
            ShardId = shardId,
            MessageId = messageId ?? Guid.NewGuid().ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            Body = Guid.NewGuid().ToString()
        };
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenBatchComplete_MovesTrackerReleasesLockAndRemovesSession()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lockHandle = CreateAcquiredLock();
        var job1 = CreateKinesisJob("shard-a", "msg-1");
        var job2 = CreateKinesisJob("shard-a", "msg-2");

        checkpointStorage.Setup(c =>
                c.UpdateShortTermAsync("shard-a", "iterator-2", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        checkpointStorage.Setup(c =>
                c.UpdateLongTermAsync("shard-a", "seq-2", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        lockHandle.Setup(l => l.UnlockAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var streamSource = CreateStreamSource(checkpointStorage);
        streamSource.Sessions["shard-a"] = new KinesisTrackerSession("shard-a",
            new StreamSourceResponse
            {
                IteratorString = "iterator-2",
                LastSequenceNumber = "seq-2",
                Items = [job1, job2]
            }, lockHandle.Object);

        await streamSource.AcknowledgeAsync(job1, CoreJobResult.Failure,
            TestContext.Current.CancellationToken);
        Assert.True(streamSource.Sessions.ContainsKey("shard-a"));

        await streamSource.AcknowledgeAsync(job2, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.False(streamSource.Sessions.ContainsKey("shard-a"));
        checkpointStorage.Verify(
            c => c.UpdateShortTermAsync("shard-a", "iterator-2", TestContext.Current.CancellationToken), Times.Once);
        checkpointStorage.Verify(c => c.UpdateLongTermAsync("shard-a", "seq-2", TestContext.Current.CancellationToken),
            Times.Once);
        lockHandle.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenBatchIncomplete_KeepsSessionAndLock()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lockHandle = CreateAcquiredLock();
        var job1 = CreateKinesisJob("shard-a", "msg-1");
        var job2 = CreateKinesisJob("shard-a", "msg-2");

        var streamSource = CreateStreamSource(checkpointStorage);
        streamSource.Sessions["shard-a"] = new KinesisTrackerSession("shard-a",
            new StreamSourceResponse
            {
                IteratorString = "iterator-2",
                LastSequenceNumber = "seq-2",
                Items = [job1, job2]
            }, lockHandle.Object);

        await streamSource.AcknowledgeAsync(job1, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.True(streamSource.Sessions.ContainsKey("shard-a"));
        Assert.False(streamSource.Sessions["shard-a"].IsComplete);
        lockHandle.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(checkpointStorage.Invocations);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenMessageIsNotKinesisJobModel_DoesNothing()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lockHandle = CreateAcquiredLock();
        var streamSource = CreateStreamSource(checkpointStorage);
        streamSource.Sessions["shard-a"] = new KinesisTrackerSession("shard-a",
            new StreamSourceResponse
            {
                IteratorString = "iterator",
                LastSequenceNumber = "seq",
                Items = [CreateKinesisJob("shard-a")]
            }, lockHandle.Object);

        var nonKinesisJob = new Mock<IRawJobModel>(MockBehavior.Strict).Object;

        await streamSource.AcknowledgeAsync(nonKinesisJob, CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.True(streamSource.Sessions.ContainsKey("shard-a"));
        lockHandle.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(checkpointStorage.Invocations);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenShardSessionMissing_DoesNothing()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var streamSource = CreateStreamSource(checkpointStorage);

        await streamSource.AcknowledgeAsync(CreateKinesisJob("missing-shard"), CoreJobResult.Success,
            TestContext.Current.CancellationToken);

        Assert.Empty(streamSource.Sessions);
        Assert.Empty(checkpointStorage.Invocations);
    }

    [Fact]
    public async Task GetJobsAsync_SkipsShardsWithActiveSessions()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        var secondLock = CreateAcquiredLock();
        var secondJob = CreateKinesisJob("shard-b");

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["shard-a", "shard-b"]);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-b"), TestContext.Current.CancellationToken))
            .ReturnsAsync(secondLock.Object);
        checkpointStorage.Setup(c => c.GetCheckpointAsync("shard-b", TestContext.Current.CancellationToken))
            .ReturnsAsync("iterator-b");
        lowLevelStreamSource.Setup(l =>
                l.GetJobsAsync(1, "shard-b", "iterator-b", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "iterator-b-2",
                LastSequenceNumber = "seq-b",
                Items = [secondJob]
            });

        var streamSource = CreateStreamSource(checkpointStorage, lister, locker, lowLevelStreamSource);
        streamSource.Sessions["shard-a"] = new KinesisTrackerSession("shard-a",
            new StreamSourceResponse
            {
                IteratorString = "existing",
                LastSequenceNumber = "existing-seq",
                Items = [CreateKinesisJob("shard-a")]
            }, CreateAcquiredLock().Object);

        var response = await streamSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Same(secondJob, Assert.Single(response.Items));
        Assert.Equal(2, streamSource.Sessions.Count);
        Assert.True(streamSource.Sessions.ContainsKey("shard-b"));
        locker.Verify(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-a"), It.IsAny<CancellationToken>()), Times.Never);
        locker.Verify(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-b"), TestContext.Current.CancellationToken),
            Times.Once);
        // Session retained for shard-b — lock must stay held.
        secondLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_WhenEmptyShardThenShardWithJobs_AdvancesPastEmptyAndReturnsJobs()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        var emptyLock = CreateAcquiredLock();
        var jobLock = CreateAcquiredLock();
        var job = CreateKinesisJob("shard-b");

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["shard-empty", "shard-b"]);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-empty"), TestContext.Current.CancellationToken))
            .ReturnsAsync(emptyLock.Object);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-b"), TestContext.Current.CancellationToken))
            .ReturnsAsync(jobLock.Object);
        checkpointStorage.Setup(c => c.GetCheckpointAsync("shard-empty", TestContext.Current.CancellationToken))
            .ReturnsAsync("iterator-empty");
        checkpointStorage.Setup(c => c.GetCheckpointAsync("shard-b", TestContext.Current.CancellationToken))
            .ReturnsAsync("iterator-b");
        lowLevelStreamSource.Setup(l =>
                l.GetJobsAsync(3, "shard-empty", "iterator-empty", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "iterator-empty-2",
                LastSequenceNumber = "seq-empty",
                Items = []
            });
        lowLevelStreamSource.Setup(l =>
                l.GetJobsAsync(3, "shard-b", "iterator-b", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "iterator-b-2",
                LastSequenceNumber = "seq-b",
                Items = [job]
            });
        checkpointStorage.Setup(c =>
                c.UpdateShortTermAsync("shard-empty", "iterator-empty-2", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        checkpointStorage.Setup(c =>
                c.UpdateLongTermAsync("shard-empty", "seq-empty", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var streamSource = CreateStreamSource(checkpointStorage, lister, locker, lowLevelStreamSource);

        var response = await streamSource.GetJobsAsync(3, TestContext.Current.CancellationToken);

        Assert.Same(job, Assert.Single(response.Items));
        Assert.True(streamSource.Sessions.ContainsKey("shard-b"));
        Assert.False(streamSource.Sessions.ContainsKey("shard-empty"));
        emptyLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
        jobLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_WhenFirstShardLockNotAcquired_UsesNextShardWithJobs()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        var unavailableLock = CreateUnacquiredLock();
        var acquiredLock = CreateAcquiredLock();
        var job = CreateKinesisJob("shard-b");

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["shard-a", "shard-b"]);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-a"), TestContext.Current.CancellationToken))
            .ReturnsAsync(unavailableLock.Object);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-b"), TestContext.Current.CancellationToken))
            .ReturnsAsync(acquiredLock.Object);
        checkpointStorage.Setup(c => c.GetCheckpointAsync("shard-b", TestContext.Current.CancellationToken))
            .ReturnsAsync("iterator-b");
        lowLevelStreamSource.Setup(l =>
                l.GetJobsAsync(2, "shard-b", "iterator-b", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "iterator-b-2",
                LastSequenceNumber = "seq-b",
                Items = [job]
            });

        var streamSource = CreateStreamSource(checkpointStorage, lister, locker, lowLevelStreamSource);

        var response = await streamSource.GetJobsAsync(2, TestContext.Current.CancellationToken);

        Assert.Same(job, Assert.Single(response.Items));
        Assert.True(streamSource.Sessions.ContainsKey("shard-b"));
        Assert.False(streamSource.Sessions.ContainsKey("shard-a"));
        checkpointStorage.Verify(c => c.GetCheckpointAsync("shard-a", It.IsAny<CancellationToken>()), Times.Never);
        unavailableLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
        acquiredLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task GetJobsAsync_WhenLockAcquiredAndJobsExist_RegistersSessionAndKeepsLock(int batchSize)
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        var lockHandle = CreateAcquiredLock();
        var job = CreateKinesisJob("shard-a");

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["shard-a"]);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-a"), TestContext.Current.CancellationToken))
            .ReturnsAsync(lockHandle.Object);
        checkpointStorage.Setup(c => c.GetCheckpointAsync("shard-a", TestContext.Current.CancellationToken))
            .ReturnsAsync("iterator-1");
        lowLevelStreamSource.Setup(l =>
                l.GetJobsAsync(batchSize, "shard-a", "iterator-1", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "iterator-2",
                LastSequenceNumber = "seq-1",
                Items = [job]
            });

        var streamSource = CreateStreamSource(checkpointStorage, lister, locker, lowLevelStreamSource);

        var response = await streamSource.GetJobsAsync(batchSize, TestContext.Current.CancellationToken);

        Assert.Same(job, Assert.Single(response.Items));
        Assert.True(streamSource.Sessions.ContainsKey("shard-a"));
        Assert.Equal(1, streamSource.Sessions["shard-a"].Count);

        locker.Verify(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-a"), TestContext.Current.CancellationToken),
            Times.Once);
        checkpointStorage.Verify(c => c.GetCheckpointAsync("shard-a", TestContext.Current.CancellationToken),
            Times.Once);
        lowLevelStreamSource.Verify(
            l => l.GetJobsAsync(batchSize, "shard-a", "iterator-1", TestContext.Current.CancellationToken), Times.Once);
        lockHandle.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Never);
        checkpointStorage.Verify(
            c => c.UpdateShortTermAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        checkpointStorage.Verify(
            c => c.UpdateLongTermAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetJobsAsync_WhenNoJobsAndBlankSequenceNumber_UpdatesShortTermOnlyThenUnlocks()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        var emptyShardLock = CreateAcquiredLock();

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["shard-empty"]);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-empty"), TestContext.Current.CancellationToken))
            .ReturnsAsync(emptyShardLock.Object);
        checkpointStorage.Setup(c => c.GetCheckpointAsync("shard-empty", TestContext.Current.CancellationToken))
            .ReturnsAsync("iterator-empty");
        lowLevelStreamSource.Setup(l =>
                l.GetJobsAsync(1, "shard-empty", "iterator-empty", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "iterator-empty-2",
                LastSequenceNumber = "   ",
                Items = []
            });
        checkpointStorage.Setup(c =>
                c.UpdateShortTermAsync("shard-empty", "iterator-empty-2", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var streamSource = CreateStreamSource(checkpointStorage, lister, locker, lowLevelStreamSource);

        var response = await streamSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Empty(streamSource.Sessions);
        checkpointStorage.Verify(
            c => c.UpdateShortTermAsync("shard-empty", "iterator-empty-2", TestContext.Current.CancellationToken),
            Times.Once);
        checkpointStorage.Verify(
            c => c.UpdateLongTermAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        emptyShardLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_WhenNoJobs_MovesTrackerUnlocksAndReturnsEmpty()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        var emptyShardLock = CreateAcquiredLock();

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["shard-empty"]);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-empty"), TestContext.Current.CancellationToken))
            .ReturnsAsync(emptyShardLock.Object);
        checkpointStorage.Setup(c => c.GetCheckpointAsync("shard-empty", TestContext.Current.CancellationToken))
            .ReturnsAsync("iterator-empty");
        lowLevelStreamSource.Setup(l =>
                l.GetJobsAsync(1, "shard-empty", "iterator-empty", TestContext.Current.CancellationToken))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "iterator-empty-2",
                LastSequenceNumber = "seq-empty",
                Items = []
            });
        checkpointStorage.Setup(c =>
                c.UpdateShortTermAsync("shard-empty", "iterator-empty-2", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        checkpointStorage.Setup(c =>
                c.UpdateLongTermAsync("shard-empty", "seq-empty", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var streamSource = CreateStreamSource(checkpointStorage, lister, locker, lowLevelStreamSource);

        var response = await streamSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Empty(streamSource.Sessions);
        checkpointStorage.Verify(
            c => c.UpdateShortTermAsync("shard-empty", "iterator-empty-2", TestContext.Current.CancellationToken),
            Times.Once);
        checkpointStorage.Verify(
            c => c.UpdateLongTermAsync("shard-empty", "seq-empty", TestContext.Current.CancellationToken), Times.Once);
        emptyShardLock.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetJobsAsync_WhenNoLocksAcquired_ReturnsEmptyWithoutReadingCheckpoints()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLockService>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        var lockA = CreateUnacquiredLock();
        var lockB = CreateUnacquiredLock();

        lister.Setup(l => l.GetListOfShardsAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(["shard-a", "shard-b"]);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-a"), TestContext.Current.CancellationToken))
            .ReturnsAsync(lockA.Object);
        locker.Setup(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-b"), TestContext.Current.CancellationToken))
            .ReturnsAsync(lockB.Object);

        var streamSource = CreateStreamSource(checkpointStorage, lister, locker, lowLevelStreamSource);

        var response = await streamSource.GetJobsAsync(1, TestContext.Current.CancellationToken);

        Assert.Empty(response.Items);
        Assert.Empty(streamSource.Sessions);
        locker.Verify(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-a"), TestContext.Current.CancellationToken),
            Times.Once);
        locker.Verify(l => l.GetLockAsync(KeyHelper.GetLockKey("shard-b"), TestContext.Current.CancellationToken),
            Times.Once);
        lockA.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
        lockB.Verify(l => l.UnlockAsync(It.IsAny<CancellationToken>()), Times.Once);
        checkpointStorage.Verify(
            c => c.GetCheckpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        lowLevelStreamSource.Verify(
            l => l.GetJobsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HeartbeatAsync_IsNoOp()
    {
        var streamSource = CreateStreamSource();

        await streamSource.HeartbeatAsync(CreateKinesisJob("shard-a"),
            TestContext.Current.CancellationToken);

        Assert.Empty(streamSource.Sessions);
    }

    [Fact]
    public async Task MoveTrackerAsync_UpdatesShortAndLongTermCheckpoints()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        checkpointStorage.Setup(c =>
                c.UpdateShortTermAsync("shard-x", "iterator-x", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);
        checkpointStorage.Setup(c =>
                c.UpdateLongTermAsync("shard-x", "seq-x", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var streamSource = CreateStreamSource(checkpointStorage);
        var session = new KinesisTrackerSession("shard-x",
            new StreamSourceResponse
            {
                IteratorString = "iterator-x",
                LastSequenceNumber = "seq-x",
                Items = []
            }, CreateAcquiredLock().Object);

        await streamSource.MoveTrackerAsync(session, TestContext.Current.CancellationToken);

        checkpointStorage.Verify(
            c => c.UpdateShortTermAsync("shard-x", "iterator-x", TestContext.Current.CancellationToken), Times.Once);
        checkpointStorage.Verify(c => c.UpdateLongTermAsync("shard-x", "seq-x", TestContext.Current.CancellationToken),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MoveTrackerAsync_WhenSequenceNumberMissing_UpdatesShortTermOnly(string? sequenceNumber)
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        checkpointStorage.Setup(c =>
                c.UpdateShortTermAsync("shard-x", "iterator-x", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var streamSource = CreateStreamSource(checkpointStorage);
        var session = new KinesisTrackerSession("shard-x",
            new StreamSourceResponse
            {
                IteratorString = "iterator-x",
                LastSequenceNumber = sequenceNumber,
                Items = []
            }, CreateAcquiredLock().Object);

        await streamSource.MoveTrackerAsync(session, TestContext.Current.CancellationToken);

        checkpointStorage.Verify(
            c => c.UpdateShortTermAsync("shard-x", "iterator-x", TestContext.Current.CancellationToken), Times.Once);
        checkpointStorage.Verify(
            c => c.UpdateLongTermAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void RecommendedHeartbeatIntervalSeconds_IsZero()
    {
        var streamSource = CreateStreamSource();

        Assert.Equal(0, streamSource.RecommendedHeartbeatIntervalSeconds);
    }

    [Fact]
    public void StopSubscriber_ThrowsNotSupportedException()
    {
        var streamSource = CreateStreamSource();

        Assert.False(streamSource.IsSubscriptionSource);
        Assert.Throws<NotSupportedException>(streamSource.StopSubscriber);
    }

    private sealed class PassthroughRetryWrapper : IKinesisRetryWrapperService
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }

        public Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }
    }
}