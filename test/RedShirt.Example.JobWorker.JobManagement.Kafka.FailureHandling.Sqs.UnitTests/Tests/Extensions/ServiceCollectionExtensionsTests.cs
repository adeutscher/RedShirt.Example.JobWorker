using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Extensions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData("https://sqs.us-east-1.amazonaws.com/123456789012/kafka-failures")]
    [InlineData("https://sqs.us-west-2.amazonaws.com/123456789012/other-kafka-failures")]
    public void AddKafkaSqsFailureHandling_ConfiguresSqsQueueFailureHandler(string queueUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:Kafka:Failures:QueueUrl"] = queueUrl
            })
            .Build();

        var services = new ServiceCollection()
            .AddKafkaSqsFailureHandling(configuration);

        using var provider = services.BuildServiceProvider();

        var failures = provider.GetRequiredService<IOptions<SqsQueueFailureHandler.ConfigurationModel>>().Value;
        Assert.Equal(queueUrl, failures.QueueUrl);
    }

    [Fact]
    public void AddKafkaSqsFailureHandling_RegistersExpectedServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:Kafka:Failures:QueueUrl"] = "https://sqs.example/queue"
            })
            .Build();

        var services = new ServiceCollection()
            .AddKafkaSqsFailureHandling(configuration);

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IJobFailureHandler) &&
            d.ImplementationType == typeof(SqsQueueFailureHandler) &&
            d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IAmazonSQS));
        Assert.Contains(services, d => d.ServiceType == typeof(ISqsRetryWrapperService));
    }
}