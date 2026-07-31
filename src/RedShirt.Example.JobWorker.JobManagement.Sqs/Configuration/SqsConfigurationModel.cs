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
    ///     Configuration indication that the SQS queue is not configured in AWS with a DLQ.
    ///     I strongly encourage anyone reading this to configure a DLQ for every SQS queue.
    ///     In the event that this is set to true, then this job source implementation will attempt to take more actions to
    ///     deal with poison messages.
    ///     Calling this DlqNotEnabled does create a bit of a double-negative situation, but this is considered acceptable as
    ///     my overall goal is to nudge the developer to configure a DLQ at the AWS level.
    /// </summary>
    public required bool DlqNotEnabled { get; init; }

    /// <summary>
    ///     This is used in the event that the DlqNotEnabled property is set to true.
    ///     Attempt to give received messages a chance to be reprocessed.
    ///     Once again, I strongly encourage anyone reading this to configure a DLQ for every SQS queue.
    /// </summary>
    public required int MaximumReceives { get; init; }

    public int EffectiveMaximumReceives => Math.Max(1, MaximumReceives);

    public int EffectiveVisibilityTimeoutSeconds =>
        Math.Min(Math.Max(20, VisibilityTimeoutSeconds), MaximumVisibilityTimeoutAmountSeconds);
}