using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Sqs.Services;

public class NoReactionJobSourceBootstrapper : IJobSourceBootstrapper
{
    public void Bootstrap()
    {
        // Nothing to do for SQS
    }
}