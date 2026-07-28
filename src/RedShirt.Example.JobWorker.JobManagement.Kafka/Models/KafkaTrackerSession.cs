namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal class KafkaTrackerSession(IReadOnlyList<IKafkaMessageContainer> messages, IKafkaMessageContainer lastMessage)
{
    private readonly HashSet<string> _acknowledgedMessageIds = new();

    private readonly HashSet<string> _sessionMessageIds =
        messages.Select(m => m.MessageId).ToHashSet();

    public IKafkaMessageContainer? LastMessage => lastMessage;
    public IReadOnlyList<IKafkaMessageContainer> Messages { get; } = messages;

    public bool IsComplete => _acknowledgedMessageIds.Count >= _sessionMessageIds.Count;

    public void Increment(string messageId)
    {
        if (_sessionMessageIds.Contains(messageId))
        {
            _acknowledgedMessageIds.Add(messageId);
        }
    }
}