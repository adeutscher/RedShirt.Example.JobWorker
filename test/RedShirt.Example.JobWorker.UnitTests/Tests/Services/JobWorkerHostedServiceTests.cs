using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Services;

namespace RedShirt.Example.JobWorker.UnitTests.Tests.Services;

public class JobWorkerHostedServiceTests
{
    [Fact]
    public async Task StartAsync_InvokesHandlerHandleAsync()
    {
        var handler = new Mock<IHandler>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Setup(h => h.HandleAsync(It.IsAny<CancellationToken>())).Returns(completion.Task);

        var service = new JobWorkerHostedService(handler.Object);

        var startTask = service.StartAsync(CancellationToken.None);

        handler.Verify(h => h.HandleAsync(It.IsAny<CancellationToken>()), Times.Once);

        completion.SetResult();
        await service.StopAsync(CancellationToken.None);
        await startTask;
    }
}
