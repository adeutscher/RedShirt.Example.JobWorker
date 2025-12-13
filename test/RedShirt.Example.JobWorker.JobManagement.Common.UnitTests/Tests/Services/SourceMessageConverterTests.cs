using RedShirt.Example.JobWorker.JobManagement.Common.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.JobManagement.Common.UnitTests.Tests.Services;

public class SourceMessageConverterTests
{
    /// <summary>
    ///     Confirm conversion
    /// </summary>
    [Fact]
    public void TestConvert()
    {
        var input = new JobDataModel
        {
            SleepDurationSeconds = 123
        };

        var converter = new SourceMessageConverter();
        var output = converter.Convert(JsonSerializer.Serialize(input));

        Assert.NotNull(output);
        Assert.Equal(input.SleepDurationSeconds, output.SleepDurationSeconds);
    }

    /// <summary>
    ///     Confirm that the converter isn't case-sensitive.
    /// </summary>
    [Fact]
    public void TestConvert_CaseInsensitive()
    {
        var input = new JobDataModel
        {
            SleepDurationSeconds = 123
        };

        var converter = new SourceMessageConverter();
        var output = converter.Convert(JsonSerializer.Serialize(input).ToLower());

        Assert.NotNull(output);
        Assert.Equal(input.SleepDurationSeconds, output.SleepDurationSeconds);
    }
}