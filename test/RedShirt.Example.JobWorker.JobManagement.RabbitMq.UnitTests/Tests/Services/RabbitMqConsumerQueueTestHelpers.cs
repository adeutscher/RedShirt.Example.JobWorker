using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Constants;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

internal static class RabbitMqConsumerQueueTestHelpers
{
    public static IRabbitMqConsumerQueueService Create(string queueName)
    {
        var mock = new Mock<IRabbitMqConsumerQueueService>(MockBehavior.Strict);
        mock.SetupGet(s => s.ConsumerQueueName).Returns(queueName);
        mock.SetupGet(s => s.ConsumerQueueNameForLogging).Returns(queueName);
        mock.SetupGet(s => s.IsUsingExchange).Returns(false);
        mock.SetupGet(s => s.ExchangeNameForLogging)
            .Returns(RabbitMqConsumerQueueLoggingConstants.NotDeclaredLoggingPlaceholder);
        mock.Setup(s => s.PrepareConsumerQueueAsync(It.IsAny<IChannel>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.ResetPreparedQueue());
        return mock.Object;
    }

    public static IRabbitMqConsumerQueueService CreateLoose(string queueName = "jobs")
    {
        var mock = new Mock<IRabbitMqConsumerQueueService>(MockBehavior.Loose);
        mock.SetupGet(s => s.ConsumerQueueName).Returns(queueName);
        mock.SetupGet(s => s.ConsumerQueueNameForLogging).Returns(queueName);
        mock.SetupGet(s => s.IsUsingExchange).Returns(false);
        mock.Setup(s => s.PrepareConsumerQueueAsync(It.IsAny<IChannel>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }
}
