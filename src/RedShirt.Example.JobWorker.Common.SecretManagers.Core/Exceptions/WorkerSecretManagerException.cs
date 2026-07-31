namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

public sealed class WorkerSecretManagerException : Exception
{
    public bool IsCritical { get; private set; }
    public bool IsTransient { get; private set; }

    public WorkerSecretManagerException(Exception innerException, bool isCritical = true, bool isTransient = false) :
        base(innerException.Message, innerException)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }

    public WorkerSecretManagerException(string message, bool isCritical = true, bool isTransient = false) :
        base(message)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
    }
}