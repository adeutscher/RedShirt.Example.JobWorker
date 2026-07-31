using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.UnitTests.Tests;

public class RunnerTests
{
    [Fact]
    public async Task Test_RunAsync()
    {
        var handler = new Mock<IHandler>();

        var runner = new Runner(handler.Object);
        await runner.RunAsync();

        Assert.Single(handler.Invocations);
    }
}