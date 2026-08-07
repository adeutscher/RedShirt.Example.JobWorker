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
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler
            .Setup(h => h.HandleAsync(It.IsAny<CancellationToken>()))
            .Callback(() => invoked.TrySetResult())
            .Returns(completion.Task);

        var service = new JobWorkerHostedService(handler.Object);

        var startTask = service.StartAsync(CancellationToken.None);

        // BackgroundService.StartAsync schedules ExecuteAsync via Task.Run; wait for the handler
        // before verifying so the assertion is not racy under load (e.g. docker build).
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        handler.Verify(h => h.HandleAsync(It.IsAny<CancellationToken>()), Times.Once);

        completion.SetResult();
        await service.StopAsync(CancellationToken.None);
        await startTask;
    }
}
