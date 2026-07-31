namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public sealed class WorkerDistributedException : Exception
{
    public bool IsCritical { get; private init; }
    public bool IsTransient { get; private init; }

    public WorkerDistributedException(Exception innerException, bool isCritical = true, bool isTransient = false) :
        base(innerException.Message,
            innerException)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }

    public WorkerDistributedException(string message, bool isCritical = true, bool isTransient = false) : base(message)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }
}