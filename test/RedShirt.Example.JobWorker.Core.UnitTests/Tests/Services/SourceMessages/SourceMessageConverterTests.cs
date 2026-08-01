using Microsoft.Extensions.Logging.Abstractions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.SourceMessages;

public class SourceMessageConverterTests
{
    private static SourceMessageConverter CreateConverter()
    {
        return new SourceMessageConverter(NullLogger<SourceMessageConverter>.Instance);
    }

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

        var converter = CreateConverter();
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

        var converter = CreateConverter();
        var output = converter.Convert(JsonSerializer.Serialize(input).ToLower());

        Assert.NotNull(output);
        Assert.Equal(input.SleepDurationSeconds, output.SleepDurationSeconds);
    }

    [Fact]
    public void TestConvert_InvalidJson_ThrowsJsonException()
    {
        var converter = CreateConverter();

        Assert.Throws<JsonException>(() => converter.Convert("not-json"));
    }

    [Fact]
    public void TestConvert_NullBytesPrefix_ThrowsJsonException()
    {
        // Mimics a mis-read ActiveMQ BytesMessage body that starts with NUL.
        var converter = CreateConverter();

        Assert.Throws<JsonException>(() => converter.Convert("\0{\"SleepDurationSeconds\":1}"));
    }
}