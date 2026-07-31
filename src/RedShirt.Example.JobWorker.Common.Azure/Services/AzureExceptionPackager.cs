using RedShirt.Example.JobWorker.Common.Azure.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Azure.Services;

public interface IAzureExceptionPackager
{
    AzureExceptionWrapper Pack(Exception exception);
}

public class AzureExceptionPackager(IAzureExceptionArbiterService arbiter) : IAzureExceptionPackager
{
    public AzureExceptionWrapper Pack(Exception exception)
    {
        var judgement = arbiter.GetJudgement(exception);
        throw new AzureExceptionWrapper(exception, judgement.IsTransient);
    }
}