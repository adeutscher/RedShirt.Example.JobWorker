namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Services;

public interface IAzureSleepService
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public class AzureSleepService : IAzureSleepService
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}