using Apache.NMS;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Wrappers;

public class ActiveMqConnectionWrapperTests
{
    [Fact]
    public async Task Test_CreateConnectionAsync()
    {
        var mockConnection = new Mock<IConnection>();

        var connectionFactory = new Mock<IConnectionFactory>(MockBehavior.Strict);
        connectionFactory.Setup(f => f.CreateConnectionAsync())
            .ReturnsAsync(mockConnection.Object);

        var wrapper = new ActiveMqConnectionWrapper(connectionFactory.Object);
        var returnedConnection = await wrapper.CreateConnectionAsync(TestContext.Current.CancellationToken);

        // Returned the same object
        Assert.Same(mockConnection.Object, returnedConnection);
        // No sudden calls to the connection
        Assert.Empty(mockConnection.Invocations);
    }
}