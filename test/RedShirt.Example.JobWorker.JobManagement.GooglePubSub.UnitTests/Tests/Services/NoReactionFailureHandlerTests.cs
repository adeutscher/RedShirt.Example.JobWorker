using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class NoReactionFailureHandlerTests
{
    [Fact]
    public async Task ShouldCompleteWithoutAction()
    {
        var handler = new NoReactionFailureHandler();
        await handler.HandleFailureAsync(new Mock<IRawJobModel>().Object, FailureType.Execution,
            new Exception("boom"), TestContext.Current.CancellationToken);
    }
}