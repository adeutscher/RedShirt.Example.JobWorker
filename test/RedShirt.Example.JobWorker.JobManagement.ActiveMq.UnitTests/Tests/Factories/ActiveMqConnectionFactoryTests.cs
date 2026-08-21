using Apache.NMS;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Wrappers;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Factories;

public class ActiveMqConnectionFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetConnectionAsync_PassesForceFlagToInnerFactory(bool forceNewSecretManagerPull)
    {
        var mockConnection = new Mock<IConnection>(MockBehavior.Strict);

        var mockWrapper = new Mock<IActiveConnectionWrapper>(MockBehavior.Strict);
        mockWrapper
            .Setup(w => w.CreateConnectionAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(mockConnection.Object);

        var innerFactory = new Mock<IInnerActiveMqConnectionFactory>(MockBehavior.Strict);
        innerFactory
            .Setup(i => i.GetConnectionFactoryWrapperAsync(
                forceNewSecretManagerPull,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(mockWrapper.Object);

        var factory = new ActiveMqConnectionFactory(innerFactory.Object);

        var returnedConnection = await factory.GetConnectionAsync(
            forceNewSecretManagerPull,
            TestContext.Current.CancellationToken);
        Assert.NotNull(returnedConnection);
        Assert.Same(returnedConnection, mockConnection.Object);
        innerFactory.Verify(i => i.GetConnectionFactoryWrapperAsync(
            forceNewSecretManagerPull,
            TestContext.Current.CancellationToken), Times.Once);
    }
}