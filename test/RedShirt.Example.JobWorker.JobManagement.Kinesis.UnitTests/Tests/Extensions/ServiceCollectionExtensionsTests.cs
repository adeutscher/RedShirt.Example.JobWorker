using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData("checkpoint-table", 12)]
    [InlineData("other-checkpoint-table", 1)]
    public void AddKinesisJobManagement_ConfiguresDynamoSequenceNumberStorage(string tableName,
        int recordDurationHours)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:Kinesis:Checkpoint:TableName"] = tableName,
                ["JobSource:Kinesis:Checkpoint:RecordDurationHours"] = recordDurationHours.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddKinesisJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var checkpoint = provider.GetRequiredService<IOptions<DynamoSequenceNumberStorage.ConfigurationModel>>()
            .Value;
        Assert.Equal(tableName, checkpoint.TableName);
        Assert.Equal(recordDurationHours, checkpoint.RecordDurationHours);
    }

    [Theory]
    [InlineData("arn:aws:kinesis:us-east-1:123456789012:stream/test", true, false)]
    [InlineData("arn:aws:kinesis:us-west-2:123456789012:stream/other", false, true)]
    public void AddKinesisJobManagement_ConfiguresKinesisConfiguration(string streamArn, bool roundRobinShards,
        bool shuffleShards)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:Kinesis:StreamArn"] = streamArn,
                ["JobSource:Kinesis:RoundRobinShards"] = roundRobinShards.ToString(),
                ["JobSource:Kinesis:ShuffleShards"] = shuffleShards.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddKinesisJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var kinesis = provider.GetRequiredService<IOptions<KinesisConfiguration>>().Value;
        Assert.Equal(streamArn, kinesis.StreamArn);
        Assert.Equal(roundRobinShards, kinesis.RoundRobinShards);
        Assert.Equal(shuffleShards, kinesis.ShuffleShards);
    }

    [Theory]
    [InlineData("redis/connection-string")]
    [InlineData("secrets/redis")]
    public void AddKinesisJobManagement_ConfiguresRedisConnectionFactory(string connectionStringPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:Kinesis:Redis:ConnectionStringPath"] = connectionStringPath
            })
            .Build();

        var services = new ServiceCollection()
            .AddKinesisJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var redis = provider.GetRequiredService<IOptions<RedisConnectionFactory.ConfigurationModel>>().Value;
        Assert.Equal(connectionStringPath, redis.ConnectionStringPath);
    }

    [Theory]
    [InlineData("https://sqs.us-east-1.amazonaws.com/123456789012/failures")]
    [InlineData("https://sqs.us-west-2.amazonaws.com/123456789012/other-failures")]
    public void AddKinesisJobManagement_ConfiguresSqsQueueFailureHandler(string queueUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:Kinesis:Failures:QueueUrl"] = queueUrl
            })
            .Build();

        var services = new ServiceCollection()
            .AddKinesisJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var failures = provider.GetRequiredService<IOptions<SqsQueueFailureHandler.ConfigurationModel>>().Value;
        Assert.Equal(queueUrl, failures.QueueUrl);
    }
}