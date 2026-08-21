using Microsoft.Extensions.Hosting;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Services;

namespace RedShirt.Example.JobWorker.UnitTests.Tests.Services;

public class JobWorkerHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenHandlerReturnsFalse_SetsNonZeroExitCodeAndStopsApplication()
    {
        var previousExitCode = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;

            var handler = new Mock<IHandler>();
            var hostApplicationLifetime = new Mock<IHostApplicationLifetime>(MockBehavior.Strict);
            hostApplicationLifetime.Setup(l => l.StopApplication());

            var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            handler
                .Setup(h => h.HandleAsync(It.IsAny<CancellationToken>()))
                .Callback(() => invoked.TrySetResult())
                .ReturnsAsync(false);

            var service = new JobWorkerHostedService(handler.Object, hostApplicationLifetime.Object);

            var startTask = service.StartAsync(CancellationToken.None);
            await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await service.StopAsync(CancellationToken.None);
            await startTask;

            Assert.NotEqual(0, Environment.ExitCode);
            hostApplicationLifetime.Verify(l => l.StopApplication(), Times.Once);
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }

    [Fact]
    public async Task StartAsync_InvokesHandlerHandleAsync()
    {
        var handler = new Mock<IHandler>();
        var hostApplicationLifetime = new Mock<IHostApplicationLifetime>(MockBehavior.Strict);
        hostApplicationLifetime.Setup(l => l.StopApplication());

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler
            .Setup(h => h.HandleAsync(It.IsAny<CancellationToken>()))
            .Callback(() => invoked.TrySetResult())
            .Returns(completion.Task);

        var service = new JobWorkerHostedService(handler.Object, hostApplicationLifetime.Object);

        var startTask = service.StartAsync(CancellationToken.None);

        // BackgroundService.StartAsync schedules ExecuteAsync via Task.Run; wait for the handler
        // before verifying so the assertion is not racy under load (e.g. docker build).
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        handler.Verify(h => h.HandleAsync(It.IsAny<CancellationToken>()), Times.Once);

        completion.SetResult(true);
        await service.StopAsync(CancellationToken.None);
        await startTask;

        hostApplicationLifetime.Verify(l => l.StopApplication(), Times.Once);
    }
}