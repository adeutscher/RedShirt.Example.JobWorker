namespace RedShirt.Example.JobWorker.Core.Services.Health;

/// <summary>
///     Reports whether the job worker is ready to remain in load-balancer / probe rotation.
/// </summary>
public interface IWorkerReadiness
{
    bool IsReady();
}
