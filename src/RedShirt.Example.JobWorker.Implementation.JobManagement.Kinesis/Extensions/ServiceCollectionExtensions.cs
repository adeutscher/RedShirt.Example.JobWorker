using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Kinesis;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Common.Extensions;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Configuration;
using RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Services;

namespace RedShirt.Example.JobWorker.Implementation.JobManagement.Kinesis.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAwsServiceWithLocalSupport<TService>(this IServiceCollection services)
        where TService : IAmazonService
    {
        var url = Environment.GetEnvironmentVariable("AWS_SERVICE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return services
                .AddAWSService<TService>();
        }

        // Note: S3 needs a special carve-out for AmazonS3Config.ForcePathStyle that is not needed here.

        Console.WriteLine($"Using AWS service URL: {url}");

        return services.AddAWSService<TService>(new AWSOptions
        {
            DefaultClientConfig =
            {
                ServiceURL = url
            }
        });
    }

    public static IServiceCollection AddKinesisJobManagement(this IServiceCollection services,
        IConfigurationRoot configuration)
    {
        return services
            .AddCommonJobManagement(configuration)
            .Configure<KinesisConfiguration>(configuration.GetSection("JobSource:Kinesis"))
            .AddSingleton<IAbstractedLocker, RedLocker>()
            .Configure<RedisConfiguration>(configuration.GetSection("JobSource:Kinesis:Redis"))
            .AddAwsServiceWithLocalSupport<IAmazonKinesis>()
            .AddAwsServiceWithLocalSupport<IAmazonSQS>()
            .AddAwsServiceWithLocalSupport<IAmazonDynamoDB>()
            .AddSingleton<IDynamoDBContext, DynamoDBContext>()
            .AddSingleton<ICheckpointStorage, CheckpointStorage>()
            .AddSingleton<IJobSource, HighLevelStreamSource>()
            .AddSingleton<ILowLevelStreamSource, LowLevelStreamSource>()
            .AddSingleton<IKinesisShardLister, KinesisShardLister>()
            .AddSingleton<ISequenceNumberStorage, DynamoSequenceNumberStorage>()
            .Configure<DynamoSequenceNumberStorage.ConfigurationModel>(
                configuration.GetSection("JobSource:Kinesis:Checkpoint"))
            .AddSingleton<IRedisConnectionSource, RedisConnectionSource>()
            .AddSingleton<IJobFailureHandler, NoReactionFailureHandler>();
    }
}