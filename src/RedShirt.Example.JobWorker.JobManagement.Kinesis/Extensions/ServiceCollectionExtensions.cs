using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Kinesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Common.Aws.Extensions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Extensions;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Checkpoints;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKinesisJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .Configure<KinesisConfiguration>(configuration.GetSection("JobSource:Kinesis"))
            .AddAwsServiceWithLocalSupport<IAmazonKinesis>()
            .AddSqs()
            .AddAwsServiceWithLocalSupport<IAmazonDynamoDB>()
            .AddSingleton<IKinesisExceptionArbiterService, KinesisExceptionArbiterService>()
            .AddSingleton<IKinesisRetryWrapperService, KinesisRetryWrapperService>()
            .AddSingleton<IDynamoDBContext, DynamoDBContext>()
            .AddSingleton<ICheckpointStorage, CheckpointStorage>()
            .AddSingleton<IJobSource, HighLevelStreamSource>()
            .AddSingleton<ILowLevelStreamSource, LowLevelStreamSource>()
            .AddSingleton<IKinesisShardLister, KinesisShardLister>()
            .AddSingleton<IShortTermIteratorStorage, RemoteCacheShortTermIteratorStorage>()
            .AddSingleton<ISequenceNumberStorage, DynamoSequenceNumberStorage>()
            .Configure<DynamoSequenceNumberStorage.ConfigurationModel>(
                configuration.GetSection("JobSource:Kinesis:Checkpoint"))
            .AddSingleton<IJobFailureHandler, SqsQueueFailureHandler>()
            .Configure<SqsQueueFailureHandler.ConfigurationModel>(
                configuration.GetSection("JobSource:Kinesis:Failures"));
    }
}