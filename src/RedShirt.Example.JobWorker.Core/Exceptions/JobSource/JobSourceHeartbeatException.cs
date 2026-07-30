namespace RedShirt.Example.JobWorker.Core.Exceptions.JobSource;

public class JobSourceHeartbeatException : Exception
{
    public bool IsTransient { get; private set; }

    public JobSourceHeartbeatException(Exception innerException) : base(innerException.Message, innerException)
    {
        IsTransient = true;
    }

    public JobSourceHeartbeatException(bool isTransient, Exception innerException) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }

    public JobSourceHeartbeatException(bool isTransient, string message) : base(message)
    {
        IsTransient = isTransient;
    }
}