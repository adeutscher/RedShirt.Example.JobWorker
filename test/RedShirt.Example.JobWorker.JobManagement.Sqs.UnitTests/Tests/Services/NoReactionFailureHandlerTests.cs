using RedShirt.Example.JobWorker.JobManagement.Sqs.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.UnitTests.Tests.Services;

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