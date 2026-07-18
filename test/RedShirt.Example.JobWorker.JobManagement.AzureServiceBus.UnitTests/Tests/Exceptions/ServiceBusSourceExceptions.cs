using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Exceptions;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Exceptions;

public class ServiceBusSourceExceptions
{
    [Fact]
    public void TestServiceBusSourceExceptions()
    {
        var e = new ServiceBusSourceException("foo");
        Assert.Equal("foo", e.Message);
    }
}