namespace RedShirt.Example.JobWorker.JobManagement.Sqs.Configuration;

internal sealed class SqsConfigurationModel
{
    /// <summary>
    ///     Maximum amount of time from first receive that an SQS message's in-flight time can be extended to.
    ///     This is a hard limit built into SQS, and we keep track of it here in order to manage it as best as we can.
    ///     I cannot stress enough that if you can foresee your individual job workloads exceeding 12 hours,
    ///     then perhaps SQS is just the wrong choice of message broker for your use case.
    /// </summary>
    public const int MaximumVisibilityTimeoutAmountSeconds = 43200;

    public required string QueueUrl { get; init; }
    public required int VisibilityTimeoutSeconds { get; init; }

    /// <summary>
    ///     Configuration indication that the SQS queue is configured in AWS with a DLQ.
    ///     I strongly encourage anyone reading this to configure a DLQ for every SQS queue.
    ///     In the event that this is set to false, then this job source implementation will attempt to take more actions to
    ///     deal with poison messages.
    /// </summary>
    public required bool DlqEnabled { get; init; }

    /// <summary>
    ///     This is used in the event that the DlqEnabled property is set to false.
    ///     Attempt to give received messages a chance to be reprocessed.
    ///     Once again, I strongly encourage anyone reading this to configure a DLQ for every SQS queue.
    /// </summary>
    public required int MaximumReceives { get; init; }

    public int EffectiveMaximumReceives => Math.Max(1, MaximumReceives);

    public int EffectiveVisibilityTimeoutSeconds =>
        Math.Min(Math.Max(20, VisibilityTimeoutSeconds), MaximumVisibilityTimeoutAmountSeconds);
}