namespace RedShirt.Example.JobWorker.Common.Distributed.Exceptions;

public class CacheConnectionException : CacheException
{
    /// <summary>
    ///     Generalized form of connection extension.
    /// </summary>
    /// <param name="innerException"></param>
    public CacheConnectionException(Exception innerException)
    {
    }
}