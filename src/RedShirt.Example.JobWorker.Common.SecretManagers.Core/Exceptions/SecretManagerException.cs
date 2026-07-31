namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

public sealed class SecretManagerException : Exception
{
    public bool IsTransient { get; private set; }

    public SecretManagerException(Exception innerException, bool isTransient = false) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }

    public SecretManagerException(string message, bool isTransient = false) : base(message)
    {
        IsTransient = isTransient;
    }
}