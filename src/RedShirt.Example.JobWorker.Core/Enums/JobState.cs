namespace RedShirt.Example.JobWorker.Core.Enums;

internal enum JobState
{
    Inactive,
    Active,
    BlockedByIdempotency,
    Complete
}