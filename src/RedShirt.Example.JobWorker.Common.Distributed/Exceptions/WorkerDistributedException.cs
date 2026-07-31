namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public sealed class WorkerDistributedException : Exception
{
    public bool IsTransient { get; private set; }

    public WorkerDistributedException(Exception innerException, bool isTransient = false) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }

    public WorkerDistributedException(string message, bool isTransient = false) : base(message)
    {
        IsTransient = isTransient;
    }
}