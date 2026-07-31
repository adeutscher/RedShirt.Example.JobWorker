using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Factories;
using RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Factories;

public class KafkaConsumerFactoryTests
{
    [Fact]
    public void CreateConsumer_BuildsWrapperFromConfiguration()
    {
        var factory = new KafkaConsumerFactory(
            KafkaRetryTestHelpers.CreatePassthroughRetryWrapper().Object,
            Options.Create(new KafkaConsumerFactory.ConfigurationModel
            {
                BootstrapServers = "localhost:9092",
                GroupId = "test-group",
                Topic = "test-topic"
            }));

        using var consumer = factory.CreateConsumer();
        Assert.NotNull(consumer);
    }
}