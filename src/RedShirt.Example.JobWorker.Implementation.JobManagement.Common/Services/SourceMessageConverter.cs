using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Models;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Services;

/// <summary>
///     Convert from raw string data into a job model.
/// </summary>
public interface ISourceMessageConverter
{
    IJobDataModel? Convert(string input);
}

public class SourceMessageConverter : ISourceMessageConverter
{
    public IJobDataModel? Convert(string input)
    {
        return JsonSerializer.Deserialize<JobDataModel>(input);
    }
}