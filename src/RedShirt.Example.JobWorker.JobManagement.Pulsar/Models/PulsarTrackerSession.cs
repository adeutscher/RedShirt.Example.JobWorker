namespace RedShirt.Example.JobWorker.JobManagement.Pulsar.Models;

internal class PulsarTrackerSession(
    IReadOnlyList<IPulsarMessageContainer> totalMessages,
    IReadOnlyList<IPulsarMessageContainer> messagesToProcess)
{
    private readonly HashSet<string> _acknowledgedMessageIds = [];

    private readonly HashSet<string> _sessionMessageIds =
        messagesToProcess.Select(m => m.MessageId).ToHashSet();

    public IReadOnlyList<IPulsarMessageContainer> MessagesToProcess { get; } = messagesToProcess;
    public IReadOnlyList<IPulsarMessageContainer> TotalMessages { get; } = totalMessages;

    public bool IsComplete => _acknowledgedMessageIds.Count >= _sessionMessageIds.Count;

    public void Increment(string messageId)
    {
        if (_sessionMessageIds.Contains(messageId))
        {
            _acknowledgedMessageIds.Add(messageId);
        }
    }
}
