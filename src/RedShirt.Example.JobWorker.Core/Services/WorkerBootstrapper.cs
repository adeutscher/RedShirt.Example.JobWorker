namespace RedShirt.Example.JobWorker.Core.Services;

public class WorkerBootstrapper(IJobSourceBootstrapper jobSourceBootstrapper)
{
    public void BootstrapAsync(CancellationToken cancellationToken = default)
    {
        jobSourceBootstrapper.Bootstrap();
    }
}