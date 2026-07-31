namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public class CacheConnectionException : CacheException
{
    /// <summary>
    ///     Generalized form of connection exception.
    /// </summary>
    /// <param name="innerException"></param>
    public CacheConnectionException(Exception innerException)
        : base(innerException.Message, innerException)
    {
    }
}