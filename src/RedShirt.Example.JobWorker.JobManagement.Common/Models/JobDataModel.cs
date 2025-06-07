using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.JobManagement.Common.Models;

internal class JobDataModel : IJobDataModel
{
    public required int SleepDurationSeconds { get; init; }
}