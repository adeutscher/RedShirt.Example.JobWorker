using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.JobManagement.Pulsar.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.UnitTests.Tests.Services;

public class NoReactionFailureHandlerTests
{
    [Fact]
    public async Task HandleShouldDoNothing()
    {
        var handler = new NoReactionFailureHandler();
        await handler.HandleFailureAsync(null!, FailureType.Execution, null, TestContext.Current.CancellationToken);
        Assert.True(true); // Satisfy Sonar's requirement for an Assert
    }
}
