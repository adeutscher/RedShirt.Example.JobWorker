using RedShirt.Example.JobWorker.Common.Models;

namespace RedShirt.Example.JobWorker.Core.Models;

internal sealed class JobDataModel : IJobDataModel
{
    public required int SleepDurationSeconds { get; init; }
}
