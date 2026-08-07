using RedShirt.Example.JobWorker.Common.Models;

namespace RedShirt.Example.JobWorker.Core.Extensions;

internal static class JobModelExtensions
{
    extension(IJobModel jobModel)
    {
        public int HoursOld => (int) Math.Round((DateTime.UtcNow - jobModel.CreatedAtUtc).TotalHours);
    }
}