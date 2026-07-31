namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal class KafkaTrackerSession(
    List<IKafkaMessageContainer> totalMessages,
    List<IKafkaMessageContainer> messagesToProcess)
{
    private readonly HashSet<string> _acknowledgedMessageIds = [];

    private readonly HashSet<string> _sessionMessageIds =
        messagesToProcess.Select(m => m.MessageId).ToHashSet();

    public List<IKafkaMessageContainer> MessagesToProcess { get; } = messagesToProcess;
    public List<IKafkaMessageContainer> TotalMessages { get; } = totalMessages;

    public bool IsComplete => _acknowledgedMessageIds.Count >= _sessionMessageIds.Count;

    public void Increment(string messageId)
    {
        if (_sessionMessageIds.Contains(messageId))
        {
            _acknowledgedMessageIds.Add(messageId);
        }
    }
}