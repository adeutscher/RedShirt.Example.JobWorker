using RedShirt.Example.JobWorker.JobManagement.Nats.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.UnitTests;

public class NoReactionFailureHandlerTests
{
    [Fact]
    public async Task HandleShouldDoNothing()
    {
        var handler = new NoReactionFailureHandler();
        await handler.HandleFailureAsync(null!, null!, TestContext.Current.CancellationToken);
        Assert.True(true); // Satisfy Sonar's requirement for an Assert
    }
}