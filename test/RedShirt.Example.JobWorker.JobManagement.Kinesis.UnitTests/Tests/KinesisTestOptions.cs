using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests;

internal static class KinesisTestOptions
{
    public const string StreamName = "test-stream";
    public const string StreamArn = $"arn:aws:kinesis:us-east-1:000000000000:stream/{StreamName}";

    public static IOptions<KinesisConfiguration> Create(string streamArn = StreamArn)
    {
        return Options.Create(new KinesisConfiguration
        {
            StreamArn = streamArn,
            RoundRobinShards = false,
            ShuffleShards = false
        });
    }
}
