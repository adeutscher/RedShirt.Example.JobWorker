namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

public sealed class WorkerSecretManagerException : Exception
{
    public bool IsTransient { get; private set; }

    public WorkerSecretManagerException(Exception innerException, bool isTransient = false) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }

    public WorkerSecretManagerException(string message, bool isTransient = false) : base(message)
    {
        IsTransient = isTransient;
    }
}