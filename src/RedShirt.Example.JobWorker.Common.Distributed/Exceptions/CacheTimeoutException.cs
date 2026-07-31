namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public class CacheTimeoutException : CacheException
{
    /// <summary>
    ///     Generalized form of timeout exception.
    /// </summary>
    /// <param name="innerException"></param>
    public CacheTimeoutException(Exception innerException)
        : base(innerException.Message, innerException)
    {
    }
}