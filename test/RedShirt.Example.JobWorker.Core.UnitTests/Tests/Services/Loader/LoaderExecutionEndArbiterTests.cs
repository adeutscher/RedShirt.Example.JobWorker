using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Loader;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Loader;

public class LoaderExecutionEndArbiterTests
{
    [Fact]
    public async Task TestKeepRunningA()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(true);

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);

        var arbiter = new LoaderExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.ShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestKeepRunningDespiteInner()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1); // Job repository says there is still an in-flight job to be run

        var arbiter = new LoaderExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.True(await arbiter.ShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TestStopRunning()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(0); // Job repository says there are no remaining items

        var arbiter = new LoaderExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.False(await arbiter.ShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test with impossible IJobRepository output
    /// </summary>
    [Fact]
    public async Task TestStopRunningWeird()
    {
        var innerArbiter = new Mock<IExecutionEndArbiter>();
        innerArbiter
            .Setup(a => a.ShouldKeepRunning())
            .Returns(false); // Inner arbiter says no

        var jobRepository = new Mock<IJobRepository>(MockBehavior.Strict);
        jobRepository
            .Setup(r => r.GetInactiveJobCountAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(-1); // Implementation should never return negatives, but here we are

        var arbiter = new LoaderExecutionEndArbiter(innerArbiter.Object, jobRepository.Object);

        Assert.False(await arbiter.ShouldKeepRunningAsync(TestContext.Current.CancellationToken));
    }
}