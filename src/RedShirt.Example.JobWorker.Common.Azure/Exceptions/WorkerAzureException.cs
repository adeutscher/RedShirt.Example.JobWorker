namespace RedShirt.Example.JobWorker.Common.Azure.Exceptions;

public sealed class WorkerAzureException : Exception
{
    public bool IsCritical { get; init; }
    public bool IsTransient { get; init; }

    public WorkerAzureException(Exception innerException, bool isCritical = true, bool isTransient = false) : base(
        innerException.Message,
        innerException)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }

    public WorkerAzureException(string message, bool isCritical = true, bool isTransient = false) : base(message)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }
}