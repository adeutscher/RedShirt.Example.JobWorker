namespace RedShirt.Example.JobWorker.JobManagement.Kafka.Models;

internal class KafkaExceptionArbiterReport
{
    public required bool AlreadyHandled { get; init; }
    public required bool CouldBeTransient { get; init; }
    public required bool IsExpected { get; init; }
    public required bool CouldBeExternallySolvable { get; init; }
}