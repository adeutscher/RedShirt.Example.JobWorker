using RabbitMQ.Client;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Wrappers;

public class RabbitConnectionWrapperTests
{
    [Fact]
    public async Task Test_CreateConnectionAsync()
    {
        var mockConnection = new Mock<IConnection>();

        var connectionFactory = new Mock<IConnectionFactory>(MockBehavior.Strict);
        connectionFactory.Setup(f => f.CreateConnectionAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(mockConnection.Object);

        var wrapper = new RabbitConnectionWrapper(connectionFactory.Object);
        var returnedConnection = await wrapper.CreateConnectionAsync(TestContext.Current.CancellationToken);

        // Returned the same object
        Assert.Same(mockConnection.Object, returnedConnection);
        // No sudden calls to the connection
        Assert.Empty(mockConnection.Invocations);
    }
}