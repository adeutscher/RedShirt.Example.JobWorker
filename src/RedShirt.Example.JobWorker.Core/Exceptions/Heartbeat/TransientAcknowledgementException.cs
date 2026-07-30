namespace RedShirt.Example.JobWorker.Core.Exceptions;

public class TransientAcknowledgementException(Exception innerException) : Exception(innerException.Message, innerException);