using Azure.Messaging.ServiceBus;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal interface IServiceBusMessageLockExtender
{
    Task RenewMessageLockAsync(CancellationToken cancellationToken = default);
}

internal sealed class ProcessMessageLockExtender(ProcessMessageEventArgs args) : IServiceBusMessageLockExtender
{
    public Task RenewMessageLockAsync(CancellationToken cancellationToken = default) =>
        args.RenewMessageLockAsync(args.Message, cancellationToken);
}
