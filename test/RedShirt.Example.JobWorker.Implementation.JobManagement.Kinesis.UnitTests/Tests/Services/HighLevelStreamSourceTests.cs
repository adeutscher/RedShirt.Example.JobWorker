using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class HighLevelStreamSourceTests
{
    [Fact]
    public async Task Test_GetJobsAsync_InvalidOperation_Twice()
    {
        var streamSource = new HighLevelStreamSource(null!, null!, null!, null!, new NullLogger<HighLevelStreamSource>())
            {
                Lock = new Mock<IAbstractedLock>().Object
            };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await streamSource.GetJobsAsync());
    }
    
    [Fact]
    public async Task Test_GetJobsAsync_CouldNotGetLock()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);

        var cts = new CancellationTokenSource();

        lister.Setup(l => l.GetListOfShardsAsync(cts.Token))
            .ReturnsAsync(["foo", "bar"]);

        locker.Setup(l => l.GetLockAsync(It.IsAny<string>(), cts.Token))
            .ReturnsAsync(new Mock<IAbstractedLock>().Object);
        
        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object, lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>());

        var response = await streamSource.GetJobsAsync(cts.Token);
        
        locker.Verify(l=>l.GetLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        Assert.Equal(0, response.RecommendedHeartbeatIntervalSeconds);
        Assert.Empty(response.Items);
    }
    
    [Fact]
    public async Task Test_GetJobsAsync()
    {
        var checkpointStorage = new Mock<ICheckpointStorage>(MockBehavior.Strict);
        var lister = new Mock<IKinesisShardLister>(MockBehavior.Strict);
        var locker = new Mock<IAbstractedLocker>(MockBehavior.Strict);
        var lowLevelStreamSource = new Mock<ILowLevelStreamSource>(MockBehavior.Strict);
        
        var @lock = new Mock<IAbstractedLock>(MockBehavior.Strict);
        @lock.Setup(l => l.IsAcquired)
            .Returns(true);

        var jobModel = new Mock<IJobModel>().Object;

        var cts = new CancellationTokenSource();

        lister.Setup(l => l.GetListOfShardsAsync(cts.Token))
            .ReturnsAsync(["foo"]);

        locker.Setup(l => l.GetLockAsync(It.IsAny<string>(), cts.Token))
            .ReturnsAsync(@lock.Object);

        checkpointStorage.Setup(c => c.GetCheckpointAsync("foo", cts.Token))
            .ReturnsAsync("bar");

        lowLevelStreamSource.Setup(l => l.GetJobsAsync("bar", cts.Token))
            .ReturnsAsync(new StreamSourceResponse
            {
                IteratorString = "1",
                LastSequenceNumber = "2",
                Items = [
                    jobModel
                ]
            });
        
        checkpointStorage.Setup(c=>c.UpdateShortTermAsync("foo", "1", cts.Token))
            .Returns(Task.CompletedTask);
        checkpointStorage.Setup(c=>c.UpdateLongTermAsync("foo", "2", cts.Token))
            .Returns(Task.CompletedTask);
        
        var streamSource = new HighLevelStreamSource(checkpointStorage.Object, lister.Object, locker.Object, lowLevelStreamSource.Object, new NullLogger<HighLevelStreamSource>());

        var response = await streamSource.GetJobsAsync(cts.Token);
        
        locker.Verify(l=>l.GetLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(0, response.RecommendedHeartbeatIntervalSeconds);
        var item = Assert.Single(response.Items);
        Assert.Same(jobModel, item);
    }
}