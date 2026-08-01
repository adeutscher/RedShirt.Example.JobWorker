using RedShirt.Example.JobWorker.Core.Models;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Core.Services.SourceMessages;

/// <summary>
///     Convert from raw string data into a job model.
/// </summary>
internal interface ISourceMessageConverter
{
    IJobDataModel? Convert(string input);
}

internal class SourceMessageConverter : ISourceMessageConverter
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IJobDataModel? Convert(string input)
    {
        return JsonSerializer.Deserialize<JobDataModel>(input, _options);
    }
}
