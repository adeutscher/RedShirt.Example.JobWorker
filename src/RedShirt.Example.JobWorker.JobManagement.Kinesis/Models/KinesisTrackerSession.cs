namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;

internal class KinesisTrackerSession(
    string shardName,
    StreamSourceResponse streamSourceResponse,
    IAbstractedLock lockHandle)
{
    private readonly HashSet<string> _acknowledgedSequenceNumbers = new();

    private readonly HashSet<string> _sessionSequenceNumbers =
        streamSourceResponse.Items.Select(i => i.MessageId).ToHashSet();

    public int Count => _sessionSequenceNumbers.Count;

    public bool IsComplete => _acknowledgedSequenceNumbers.Count >= _sessionSequenceNumbers.Count;

    public string ShardName => shardName;

    public StreamSourceResponse StreamSourceResponse => streamSourceResponse;

    public void Increment(string sequenceNumber)
    {
        if (_sessionSequenceNumbers.Contains(sequenceNumber))
        {
            _acknowledgedSequenceNumbers.Add(sequenceNumber);
        }
    }

    public void ReleaseLockOnShard()
    {
        lockHandle.Unlock();
    }
}