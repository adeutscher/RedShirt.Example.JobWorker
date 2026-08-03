namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

/// <summary>
///     Classified failure from a secret-manager operation.
/// </summary>
public sealed class WorkerSecretManagerException : Exception
{
    /// <summary>
    ///     When <c>true</c>, a retry wrapper inside the secret-manager layer has already exhausted retries
    ///     for the underlying cause; outer retry layers should not retry again.
    /// </summary>
    public bool IsHandled { get; init; }

    public bool IsCritical { get; init; }
    public bool IsTransient { get; init; }

    public WorkerSecretManagerException(
        Exception innerException,
        bool isCritical = true,
        bool isTransient = false,
        bool isHandled = false) :
        base(innerException.Message, innerException)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
        IsHandled = isHandled;
    }

    public WorkerSecretManagerException(
        string message,
        bool isCritical = true,
        bool isTransient = false,
        bool isHandled = false) :
        base(message)
    {
        IsCritical = isCritical;
        IsTransient = isTransient;
        IsHandled = isHandled;
    }
}