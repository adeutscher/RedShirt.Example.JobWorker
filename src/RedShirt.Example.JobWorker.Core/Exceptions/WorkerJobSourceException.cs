namespace RedShirt.Example.JobWorker.Core.Exceptions;

public sealed class WorkerJobSourceException : Exception
{
    
    public bool IsCritical { get; init; }
    public bool IsTransient { get; init; }

    public WorkerJobSourceException(Exception innerException, bool isCritical = true, bool isTransient = false) : base(
        innerException.Message,
        innerException)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }

    public WorkerJobSourceException(string message, bool isCritical = true, bool isTransient = false) : base(message)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }
}
