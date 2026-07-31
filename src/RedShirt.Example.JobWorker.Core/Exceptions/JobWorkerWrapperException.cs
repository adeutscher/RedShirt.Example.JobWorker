namespace RedShirt.Example.JobWorker.Core.Exceptions;

/// <summary>
///     Made to be a convenient abstract exception for wrapping around library-specific exceptions.
/// </summary>
public abstract class JobWorkerWrapperException : Exception
{
    public bool IsTransient { get; private set; }

    protected JobWorkerWrapperException(bool isTransient, Exception innerException) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }

    protected JobWorkerWrapperException(bool isTransient, string message) : base(message)
    {
        IsTransient = isTransient;
    }
}