using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Kinesis;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKinesisJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .Configure<KinesisConfiguration>(configuration.GetSection("JobSource:Kinesis"))
            .AddSingleton<IAbstractedLocker, RedisLocker>()
            .Configure<RedisConfiguration>(configuration.GetSection("JobSource:Kinesis:Redis"))
            .AddAwsServiceWithLocalSupport<IAmazonKinesis>()
            .AddAwsServiceWithLocalSupport<IAmazonSQS>()
            .AddAwsServiceWithLocalSupport<IAmazonDynamoDB>()
            .AddSingleton<IDynamoDBContext, DynamoDBContext>()
            .AddSingleton<ICheckpointStorage, CheckpointStorage>()
            .AddSingleton<IJobSource, HighLevelStreamSource>()
            .AddSingleton<ILowLevelStreamSource, LowLevelStreamSource>()
            .AddSingleton<IKinesisShardLister, KinesisShardLister>()
            .AddSingleton<IShortTermIteratorStorage, RedisShortTermIteratorStorage>()
            .AddSingleton<ISequenceNumberStorage, DynamoSequenceNumberStorage>()
            .Configure<DynamoSequenceNumberStorage.ConfigurationModel>(
                configuration.GetSection("JobSource:Kinesis:Checkpoint"))
            .AddSingleton<IRedisConnectionSource, RedisConnectionSource>()
            .AddSingleton<IJobFailureHandler, SqsQueueFailureHandler>()
            .Configure<SqsQueueFailureHandler.ConfigurationModel>(
                configuration.GetSection("JobSource:Kinesis:Failures"));
    }
}