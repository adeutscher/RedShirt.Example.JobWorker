using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Models;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.JobManagement.Common.Services;

/// <summary>
///     Convert from raw string data into a job model.
/// </summary>
public interface ISourceMessageConverter
{
    IJobDataModel? Convert(string input);
}

internal class SourceMessageConverter : ISourceMessageConverter
{
    public IJobDataModel? Convert(string input)
    {
        return JsonSerializer.Deserialize<JobDataModel>(input);
    }
}