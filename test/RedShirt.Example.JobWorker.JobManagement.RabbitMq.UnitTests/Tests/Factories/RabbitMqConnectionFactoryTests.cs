using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Factories;

public class RabbitMqConnectionFactoryTests
{
    [Fact]
    public async Task Test_GetConnectionAsync()
    {
        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);

        var mockWrapper = new Mock<IRabbitConnectionWrapper>(MockBehavior.Strict);
        mockWrapper
            .Setup(w => w.CreateConnectionAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(mockConnection.Object);

        var innerFactory = new Mock<IInnerRabbitMqConnectionFactory>(MockBehavior.Strict);
        innerFactory
            .Setup(i => i.GetConnectionFactoryWrapperAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(mockWrapper.Object);

        var factory = new RabbitMqConnectionFactory(innerFactory.Object);

        var returnedConnection = await factory.GetConnectionAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(returnedConnection);
        Assert.Same(returnedConnection, mockConnection.Object);
    }
}