namespace RedShirt.Example.JobWorker.Common.Azure.Exceptions;

public sealed class WorkerAzureException : Exception
{
    public bool IsTransient { get; private set; }

    public WorkerAzureException(Exception innerException, bool isTransient = false) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }

    public WorkerAzureException(string message, bool isTransient = false) : base(message)
    {
        IsTransient = isTransient;
    }
}