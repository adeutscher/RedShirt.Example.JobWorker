using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services;

public class ExecutionEndArbiterTests
{
    [Fact]
    public void BasicArbiterTest()
    {
        var arbiter = new ExecutionEndArbiter(new NullLogger<ExecutionEndArbiter>());
        Assert.True(arbiter.IsRunning);
        Assert.True(arbiter.ShouldKeepRunning());

        arbiter.HandleSigTerm(null!, null!);

        Assert.False(arbiter.IsRunning);
        Assert.False(arbiter.ShouldKeepRunning());

        arbiter.Dispose(); // coverage
    }
}