namespace RedShirt.Example.JobWorker.Common.Azure.Exceptions;

public sealed class AzureExceptionWrapper : Exception
{
    public bool IsTransient { get; private set; }

    public AzureExceptionWrapper(Exception innerException, bool isTransient = false) : base(innerException.Message,
        innerException)
    {
        IsTransient = isTransient;
    }

    public AzureExceptionWrapper(string message, bool isTransient = false) : base(message)
    {
        IsTransient = isTransient;
    }
}