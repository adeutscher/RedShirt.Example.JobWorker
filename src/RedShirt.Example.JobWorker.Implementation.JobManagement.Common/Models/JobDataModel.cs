using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Models;

public class JobDataModel : IJobDataModel
{
    public required int SleepDurationSeconds { get; init; }
}