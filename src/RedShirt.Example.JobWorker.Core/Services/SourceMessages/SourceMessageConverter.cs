using RedShirt.Example.JobWorker.Core.Models;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Core.Services.SourceMessages;

/// <summary>
///     Convert from raw string data into a job model.
/// </summary>
internal interface ISourceMessageConverter
{
    /// <summary>
    ///     Convert raw message body text into a job data model.
    ///     Returning <see langword="null" /> or throwing any exception is interpreted as a parsing error
    ///     (<see cref="RedShirt.Example.JobWorker.Core.Enums.CoreJobResult.Parsing" />).
    /// </summary>
    /// <param name="input">Raw message body string to convert.</param>
    /// <returns>The parsed job data model, or <see langword="null" /> if conversion fails without throwing.</returns>
    IJobDataModel? Convert(string input);
}

internal sealed class SourceMessageConverter : ISourceMessageConverter
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Deserialize raw JSON message body text into a <see cref="JobDataModel" />.
    ///     Returning <see langword="null" /> or throwing any exception is interpreted as a parsing error
    ///     (<see cref="RedShirt.Example.JobWorker.Core.Enums.CoreJobResult.Parsing" />).
    /// </summary>
    /// <param name="input">Raw message body string to convert.</param>
    /// <returns>The parsed job data model, or <see langword="null" /> if deserialization yields no value.</returns>
    public IJobDataModel? Convert(string input)
    {
        return JsonSerializer.Deserialize<JobDataModel>(input, _options);
    }
}