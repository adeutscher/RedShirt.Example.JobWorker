using RedShirt.Example.JobWorker.Common.Aws.Sqs.Utility;
using System.Text;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.UnitTests.Tests.Utility;

public class SqsFailureMessageBuilderTests
{
    [Fact]
    public void BuildReference_IdentifiesRecordWithoutBody()
    {
        var request = SqsFailureMessageBuilder.BuildReference("queue", "topic:1:99", "SQS rejected the record body",
            new Dictionary<string, string> {["SourceShardId"] = "shard-1"});

        Assert.True(SqsFailureMessageBuilder.IsReference(request));
        Assert.Equal("true", request.MessageAttributes[SqsFailureMessageBuilder.BodyOmittedAttribute].StringValue);
        Assert.Equal("topic:1:99",
            request.MessageAttributes[SqsFailureMessageBuilder.SourceMessageIdAttribute].StringValue);

        using var document = JsonDocument.Parse(request.MessageBody);
        var root = document.RootElement;
        Assert.True(root.GetProperty("BodyOmitted").GetBoolean());
        Assert.Equal("topic:1:99", root.GetProperty("SourceMessageId").GetString());
        Assert.Equal("shard-1", root.GetProperty("SourceAttributes").GetProperty("SourceShardId").GetString());
        Assert.Equal("SQS rejected the record body", root.GetProperty("Reason").GetString());
    }

    [Fact]
    public void Build_AllowedBody_SentUnchangedWithoutAttributes()
    {
        var request = SqsFailureMessageBuilder.Build("queue", "id-1", "hello");

        Assert.Equal("queue", request.QueueUrl);
        Assert.Equal("hello", request.MessageBody);
        Assert.True(request.MessageAttributes is null || request.MessageAttributes.Count == 0);
        Assert.False(SqsFailureMessageBuilder.IsReference(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Build_EmptyBody_SentAsReference(string? body)
    {
        var request = SqsFailureMessageBuilder.Build("queue", "id-1", body);

        Assert.True(SqsFailureMessageBuilder.IsReference(request));
    }

    [Fact]
    public void Build_RejectedCharacters_SentAsBase64()
    {
        const string body = "a\u0000b 😀";
        var request = SqsFailureMessageBuilder.Build("queue", "id-1", body,
            new Dictionary<string, string> {["SourceShardId"] = "shard-1"});

        Assert.Equal(body, Encoding.UTF8.GetString(Convert.FromBase64String(request.MessageBody)));
        Assert.True(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(request.MessageBody));
        Assert.Equal("base64", request.MessageAttributes[SqsFailureMessageBuilder.BodyEncodingAttribute].StringValue);
        Assert.Equal("id-1", request.MessageAttributes[SqsFailureMessageBuilder.SourceMessageIdAttribute].StringValue);
        Assert.Equal("shard-1", request.MessageAttributes["SourceShardId"].StringValue);
        Assert.False(SqsFailureMessageBuilder.IsReference(request));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("tab\tnewline\ncarriage\r")]
    [InlineData("{\"json\": [1, 2, 3]}")]
    [InlineData("accents éè and CJK 中文")]
    [InlineData("emoji 😀 pair")]
    [InlineData("edges   ퟿  �")]
    public void ContainsOnlyAllowedCharacters_AllowedBodies(string body)
    {
        Assert.True(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(body));
    }

    /// <summary>
    ///     Lone surrogates are built here rather than passed as theory data, because theory data serialization replaces
    ///     them with U+FFFD before the test runs.
    /// </summary>
    [Fact]
    public void ContainsOnlyAllowedCharacters_LoneSurrogates_Rejected()
    {
        const char high = '\ud83d';
        const char low = '\ude00';

        Assert.False(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(new string([' ', high, ' '])));
        Assert.False(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(new string([' ', low, ' '])));
        Assert.False(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(new string([' ', high])));
        Assert.False(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(new string([low, high])));
        Assert.True(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(new string([high, low])));
    }

    [Theory]
    [InlineData("nul \u0000")]
    [InlineData("bell \u0007")]
    [InlineData("escape \u001b[31m")]
    [InlineData("noncharacter ￾")]
    [InlineData("noncharacter ￿")]
    public void ContainsOnlyAllowedCharacters_RejectedBodies(string body)
    {
        Assert.False(SqsFailureMessageBuilder.ContainsOnlyAllowedCharacters(body));
    }
}