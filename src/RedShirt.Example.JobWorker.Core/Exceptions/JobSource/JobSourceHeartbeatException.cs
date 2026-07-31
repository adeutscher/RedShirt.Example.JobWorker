namespace RedShirt.Example.JobWorker.Core.Exceptions.JobSource;

public class JobSourceHeartbeatException : JobWorkerWrapperException
{
    public JobSourceHeartbeatException(bool isTransient, Exception innerException) : base(isTransient, innerException)
    {
    }

    public JobSourceHeartbeatException(bool isTransient, string message) : base(isTransient, message)
    {
    }
}