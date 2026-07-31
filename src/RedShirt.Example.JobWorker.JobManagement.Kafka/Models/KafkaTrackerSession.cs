namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal class KafkaTrackerSession(IReadOnlyList<IKafkaMessageContainer> totalMessages, IReadOnlyList<IKafkaMessageContainer> messagesToProcess)
{
    private readonly HashSet<string> _acknowledgedMessageIds = [];

    private readonly HashSet<string> _sessionMessageIds =
        messagesToProcess.Select(m => m.MessageId).ToHashSet();
    
    public IReadOnlyList<IKafkaMessageContainer> MessagesToProcess { get; } = messagesToProcess;
    public IReadOnlyList<IKafkaMessageContainer> TotalMessages => totalMessages;

    public bool IsComplete => _acknowledgedMessageIds.Count >= _sessionMessageIds.Count;

    public void Increment(string messageId)
    {
        if (_sessionMessageIds.Contains(messageId))
        {
            _acknowledgedMessageIds.Add(messageId);
        }
    }
}