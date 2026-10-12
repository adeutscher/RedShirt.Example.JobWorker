using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Configuration;

public class RabbitMqQueueConfigurationModelTests
{
    [Fact]
    public void Properties_CanBeInitializedAndRead()
    {
        var model = new RabbitMqQueueConfigurationModel
        {
            UseExchange = true,
            QueueName = "RabbitQueue",
            TemporaryQueueExchangeName = "RabbitExchange",
            TemporaryQueueLabel = "worker"
        };

        Assert.True(model.UseExchange);
        Assert.Equal("RabbitQueue", model.QueueName);
        Assert.Equal("RabbitExchange", model.TemporaryQueueExchangeName);
        Assert.Equal("worker", model.TemporaryQueueLabel);
    }

    [Fact]
    public void Properties_DefaultToUnsetValues()
    {
        var model = new RabbitMqQueueConfigurationModel();

        Assert.False(model.UseExchange);
        Assert.Null(model.QueueName);
        Assert.Null(model.TemporaryQueueExchangeName);
        Assert.Null(model.TemporaryQueueLabel);
    }
}