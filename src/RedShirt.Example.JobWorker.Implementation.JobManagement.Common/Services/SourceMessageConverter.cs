using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Models;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Services;

public class SourceMessageConverter : ISourceMessageConverter
{
    public IJobDataModel? Convert(string input)
    {
        return JsonSerializer.Deserialize<JobDataModel>(input);
    }
}