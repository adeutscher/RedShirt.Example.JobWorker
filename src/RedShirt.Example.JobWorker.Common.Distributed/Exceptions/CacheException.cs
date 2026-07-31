namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public abstract class CacheException : Exception
{
    public bool IsTransient { get; init; }

    protected CacheException(Exception innerException, bool isTransient = false)
        : base(innerException.Message, innerException)
    {
        IsTransient = isTransient;
    }

    protected CacheException(string? message, Exception? innerException, bool isTransient = false)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }
}