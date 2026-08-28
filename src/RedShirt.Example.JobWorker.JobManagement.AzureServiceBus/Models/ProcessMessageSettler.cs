using Azure.Messaging.ServiceBus;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Models;

internal interface IServiceBusMessageSettler
{
    Task CompleteMessageAsync(CancellationToken cancellationToken = default);

    Task AbandonMessageAsync(CancellationToken cancellationToken = default);

    Task DeadLetterMessageAsync(string deadLetterReason, string? deadLetterDescription = null,
        CancellationToken cancellationToken = default);
}

internal sealed class ProcessMessageSettler(ProcessMessageEventArgs args) : IServiceBusMessageSettler
{
    public Task CompleteMessageAsync(CancellationToken cancellationToken = default) =>
        args.CompleteMessageAsync(args.Message, cancellationToken);

    public Task AbandonMessageAsync(CancellationToken cancellationToken = default) =>
        args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);

    public Task DeadLetterMessageAsync(string deadLetterReason, string? deadLetterDescription = null,
        CancellationToken cancellationToken = default) =>
        args.DeadLetterMessageAsync(args.Message, deadLetterReason, deadLetterDescription, cancellationToken);
}
