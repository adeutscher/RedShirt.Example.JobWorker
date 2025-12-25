using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Extensions;

internal static class JobModelExtensions
{
    public static int HoursOld(this IJobModel jobModel)
    {
        return (DateTime.UtcNow - jobModel.CreatedAtUtc).Hours;
    }
}