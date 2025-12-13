namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;

internal sealed class SqsConfigurationModel
{
    public required string QueueUrl { get; init; }
    public required int MessageBatchSize { get; init; }
    public required int VisibilityTimeoutSeconds { get; init; }
    public int EffectiveMessageBatchSize => Math.Max(MessageBatchSize, 1);
    public int EffectiveVisibilityTimeoutSeconds => Math.Max(20, VisibilityTimeoutSeconds);
}