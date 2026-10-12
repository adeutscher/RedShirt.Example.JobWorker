using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Constants;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class RabbitMqConsumerQueueServiceTests
{
    [Fact]
    public void ConsumerQueueName_WhenNotUsingExchange_ReturnsConfiguredQueueName()
    {
        var service = new RabbitMqConsumerQueueService(
            Options.Create(new RabbitMqQueueConfigurationModel
            {
                QueueName = "jobs"
            }),
            NullLogger<RabbitMqConsumerQueueService>.Instance);

        Assert.Equal("jobs", service.ConsumerQueueName);
    }

    [Fact]
    public void ConsumerQueueName_WhenNotUsingExchangeAndQueueNameMissing_Throws()
    {
        var service = new RabbitMqConsumerQueueService(
            Options.Create(new RabbitMqQueueConfigurationModel()),
            NullLogger<RabbitMqConsumerQueueService>.Instance);

        Assert.Throws<InvalidOperationException>(() => _ = service.ConsumerQueueName);
    }

    [Fact]
    public void ConsumerQueueName_WhenUsingExchangeBeforePrepare_Throws()
    {
        var service = new RabbitMqConsumerQueueService(
            Options.Create(new RabbitMqQueueConfigurationModel
            {
                UseExchange = true,
                TemporaryQueueExchangeName = "events"
            }),
            NullLogger<RabbitMqConsumerQueueService>.Instance);

        Assert.Throws<InvalidOperationException>(() => _ = service.ConsumerQueueName);
    }

    [Fact]
    public void ConsumerQueueNameForLogging_WhenConsumerQueueNameThrows_ReturnsNotDeclaredPlaceholder()
    {
        var service = new RabbitMqConsumerQueueService(
            Options.Create(new RabbitMqQueueConfigurationModel()),
            NullLogger<RabbitMqConsumerQueueService>.Instance);

        Assert.Equal(RabbitMqConsumerQueueLoggingConstants.NotDeclaredLoggingPlaceholder,
            service.ConsumerQueueNameForLogging);
    }

    [Fact]
    public void ExchangeNameForLogging_WhenExchangeNameMissing_ReturnsNotDeclaredPlaceholder()
    {
        var service = new RabbitMqConsumerQueueService(
            Options.Create(new RabbitMqQueueConfigurationModel
            {
                UseExchange = true
            }),
            NullLogger<RabbitMqConsumerQueueService>.Instance);

        Assert.Equal(RabbitMqConsumerQueueLoggingConstants.NotDeclaredLoggingPlaceholder,
            service.ExchangeNameForLogging);
    }

    [Fact]
    public void IsUsingExchange_ReflectsConfiguration()
    {
        var service = new RabbitMqConsumerQueueService(
            Options.Create(new RabbitMqQueueConfigurationModel
            {
                UseExchange = true,
                TemporaryQueueExchangeName = "events"
            }),
            NullLogger<RabbitMqConsumerQueueService>.Instance);

        Assert.True(service.IsUsingExchange);
        Assert.Equal("events", service.ExchangeNameForLogging);
    }
}
