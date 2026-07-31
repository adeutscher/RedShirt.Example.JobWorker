namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public sealed class WorkerDistributedException : Exception
{
    public bool IsExpected { get; private init; }
    public bool IsTransient { get; private init; }

    public WorkerDistributedException(Exception innerException, bool isExpected = false, bool isTransient = false) :
        base(innerException.Message,
            innerException)
    {
        IsExpected = isExpected;
        IsTransient = isTransient;
    }

    public WorkerDistributedException(string message, bool isExpected = false, bool isTransient = false) : base(message)
    {
        IsExpected = isExpected;
        IsTransient = isTransient;
    }
}