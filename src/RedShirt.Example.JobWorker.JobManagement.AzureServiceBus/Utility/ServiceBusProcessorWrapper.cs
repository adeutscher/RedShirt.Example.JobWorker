using Azure.Messaging.ServiceBus;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

internal interface IServiceBusProcessorWrapper : IAsyncDisposable
{
    event Func<ProcessMessageEventArgs, Task> ProcessMessageAsync;

    event Func<ProcessErrorEventArgs, Task> ProcessErrorAsync;

    Task StartProcessingAsync(CancellationToken cancellationToken = default);

    Task StopProcessingAsync(CancellationToken cancellationToken = default);
}

internal sealed class ServiceBusProcessorWrapper(ServiceBusProcessor processor, ServiceBusClient client)
    : IServiceBusProcessorWrapper
{
    public event Func<ProcessMessageEventArgs, Task> ProcessMessageAsync
    {
        add => processor.ProcessMessageAsync += value;
        remove => processor.ProcessMessageAsync -= value;
    }

    public event Func<ProcessErrorEventArgs, Task> ProcessErrorAsync
    {
        add => processor.ProcessErrorAsync += value;
        remove => processor.ProcessErrorAsync -= value;
    }

    public Task StartProcessingAsync(CancellationToken cancellationToken = default) =>
        processor.StartProcessingAsync(cancellationToken);

    public Task StopProcessingAsync(CancellationToken cancellationToken = default) =>
        processor.StopProcessingAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await processor.DisposeAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }
}
