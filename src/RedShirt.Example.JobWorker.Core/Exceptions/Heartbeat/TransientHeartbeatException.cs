namespace RedShirt.Example.JobWorker.Core.Exceptions;

public class TransientHeartbeatException(Exception innerException) : Exception(innerException.Message, innerException);