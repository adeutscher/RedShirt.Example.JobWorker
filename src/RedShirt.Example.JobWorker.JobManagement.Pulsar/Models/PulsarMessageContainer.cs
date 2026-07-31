using Pulsar.Client.Common;

namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

internal interface IPulsarMessageContainer
{
    string? Key { get; }
    string? Value { get; }
    string Topic { get; }
    int Partition { get; }
    long EntryId { get; }
    MessageId? PulsarMessageId { get; }
    string MessageId { get; }
    bool MessageIdIsDefault { get; }
}

internal class PulsarMessageContainer : IPulsarMessageContainer
{
    private const string DefaultMessageId = "UNKNOWN";

    public required MessageId? PulsarMessageId { get; init; }
    public string? Key { get; init; }
    public string? Value { get; init; }
    public required string Topic { get; init; }

    public int Partition => PulsarMessageId?.Partition ?? -1;
    public long EntryId => PulsarMessageId?.EntryId ?? -1;

    public string MessageId => PulsarMessageId is null
        ? DefaultMessageId
        : $"{Topic}:{PulsarMessageId.Partition}:{PulsarMessageId.LedgerId}:{PulsarMessageId.EntryId}";

    public bool MessageIdIsDefault => PulsarMessageId is null;
}
