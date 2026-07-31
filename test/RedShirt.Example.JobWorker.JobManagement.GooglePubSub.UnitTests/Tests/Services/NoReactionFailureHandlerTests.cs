using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.GooglePubSub.Services;

namespace RedShirt.Example.JobWorker.JobManagement.GooglePubSub.UnitTests.Tests.Services;

public class NoReactionFailureHandlerTests
{
    [Fact]
    public async Task ShouldCompleteWithoutAction()
    {
        var handler = new NoReactionFailureHandler();
        await handler.HandleFailureAsync(new Mock<IJobModel>().Object, new Exception("boom"),
            TestContext.Current.CancellationToken);
    }
}
