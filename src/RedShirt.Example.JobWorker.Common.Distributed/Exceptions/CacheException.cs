namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public abstract class CacheException : Exception
{
    protected CacheException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
