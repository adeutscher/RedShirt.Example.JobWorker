using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

/// <summary>
///     Runs background work inline so tests do not depend on thread-pool scheduling.
/// </summary>
internal sealed class InlineThreadPoolBackgroundTaskRunner : IThreadPoolBackgroundTaskRunner
{
    public void Run(Func<Task> work, CancellationToken cancellationToken)
    {
        work().GetAwaiter().GetResult();
    }
}
