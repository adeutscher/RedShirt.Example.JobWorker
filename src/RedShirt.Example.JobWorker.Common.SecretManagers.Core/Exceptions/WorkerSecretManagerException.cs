namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

public sealed class WorkerSecretManagerException : Exception
{
    public bool IsExpected { get; private set; }
    public bool IsTransient { get; private set; }

    public WorkerSecretManagerException(Exception innerException, bool isExpected = false, bool isTransient = false) :
        base(innerException.Message, innerException)
    {
        IsExpected = isExpected;
        IsTransient = isTransient;
    }

    public WorkerSecretManagerException(string message, bool isExpected = false, bool isTransient = false) :
        base(message)
    {
        IsExpected = isExpected;
        IsTransient = isTransient;
    }
}