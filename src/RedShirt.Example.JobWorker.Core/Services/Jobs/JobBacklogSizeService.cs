using Microsoft.Extensions.Options;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs;

public interface IJobBacklogSizeService
{
    int BacklogSize { get; }
}

internal class JobBacklogSizeService(IOptions<JobRepository.ConfigurationModel> options) : IJobBacklogSizeService
{
    public int BacklogSize => options.Value.EffectiveBacklogSize;
}