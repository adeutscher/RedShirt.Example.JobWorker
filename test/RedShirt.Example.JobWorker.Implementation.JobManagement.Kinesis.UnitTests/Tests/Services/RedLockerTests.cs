using RedLockNet;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.UnitTests.Tests.Services;

public class RedLockerTests
{
    [Fact]
    public void TestLock_Acquired()
    {
        var redLock = new Mock<IRedLock>();
        redLock.Setup(s => s.IsAcquired)
            .Returns(true);

        var myLock = new RedLocker.RedLockLock(redLock.Object);
        Assert.True(myLock.IsAcquired);
        redLock.Verify(v => v.IsAcquired, Times.Once);
        redLock.Verify(v => v.Dispose(), Times.Never);
        myLock.Unlock();
        redLock.Verify(v => v.Dispose(), Times.Once);
    }

    [Fact]
    public void TestLock_NotAcquired()
    {
        var redLock = new Mock<IRedLock>();
        redLock.Setup(s => s.IsAcquired)
            .Returns(false);

        var myLock = new RedLocker.RedLockLock(redLock.Object);
        Assert.False(myLock.IsAcquired);
        redLock.Verify(v => v.IsAcquired, Times.Once);
        redLock.Verify(v => v.Dispose(), Times.Never);
        myLock.Unlock();
        redLock.Verify(v => v.Dispose(), Times.Never);
    }
}