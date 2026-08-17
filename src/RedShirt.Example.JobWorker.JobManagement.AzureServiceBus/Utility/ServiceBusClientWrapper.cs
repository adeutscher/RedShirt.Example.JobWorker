using Azure.Messaging.ServiceBus;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Utility;

internal interface IServiceBusClientWrapper
{
    Task AbandonMessageAsync(IServiceBusMessageContainer messageModel, CancellationToken cancellationToken = default);
    Task CompleteMessageAsync(IServiceBusMessageContainer messageModel, CancellationToken cancellationToken = default);

    Task DeadLetterMessageAsync(IServiceBusMessageContainer messageModel, string deadLetterReason,
        string? deadLetterDescription = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<IServiceBusMessageContainer>> GetMessagesAsync(int maxMessages, int? waitTimeSeconds = null,
        CancellationToken cancellationToken = default);

    Task RenewMessageLockAsync(IServiceBusMessageContainer messageModel, CancellationToken cancellationToken = default);
}

internal class ServiceBusClientWrapper(ServiceBusReceiver receiver) : IServiceBusClientWrapper
{
    internal ServiceBusReceiver Client => receiver;

    public Task AbandonMessageAsync(IServiceBusMessageContainer messageModel,
        CancellationToken cancellationToken = default)
    {
        return Client.AbandonMessageAsync(messageModel.Message, cancellationToken: cancellationToken);
    }

    public Task DeadLetterMessageAsync(IServiceBusMessageContainer messageModel, string deadLetterReason,
        string? deadLetterDescription = null,
        CancellationToken cancellationToken = default)
    {
        return Client.DeadLetterMessageAsync(messageModel.Message, deadLetterReason, deadLetterDescription,
            cancellationToken);
    }

    public async Task<IEnumerable<IServiceBusMessageContainer>> GetMessagesAsync(int maxMessages,
        int? waitTimeSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var rawResults = await Client.ReceiveMessagesAsync(maxMessages,
            TimeSpan.FromSeconds(waitTimeSeconds is > 0 ? waitTimeSeconds.Value : 0), cancellationToken);
        return rawResults.Select<ServiceBusReceivedMessage, IServiceBusMessageContainer>(m =>
            new ServiceBusMessageContainer
            {
                Message = m
            });
    }

    public Task CompleteMessageAsync(IServiceBusMessageContainer messageModel,
        CancellationToken cancellationToken = default)
    {
        return Client.CompleteMessageAsync(messageModel.Message, cancellationToken);
    }

    public Task RenewMessageLockAsync(IServiceBusMessageContainer messageModel,
        CancellationToken cancellationToken = default)
    {
        return Client.RenewMessageLockAsync(messageModel.Message, cancellationToken);
    }

    internal class ServiceBusMessageContainer : IServiceBusMessageContainer
    {
        public required ServiceBusReceivedMessage? Message { get; init; }
    }
}