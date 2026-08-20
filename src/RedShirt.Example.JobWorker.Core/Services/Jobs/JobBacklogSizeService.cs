using Microsoft.Extensions.Options;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs;

[Obsolete("Use ICoreConfigurationService.GetBacklogSize() instead.")]
public interface IJobBacklogSizeService
{
    int BacklogSize { get; }
}

[Obsolete("Use ICoreConfigurationService.GetBacklogSize() instead.")]
internal class JobBacklogSizeService(IOptions<JobRepository.ConfigurationModel> options) : IJobBacklogSizeService
{
    public int BacklogSize => options.Value.EffectiveBacklogSize;
}
