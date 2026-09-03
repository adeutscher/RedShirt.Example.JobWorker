using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Core.UnitTests.Tests.Exceptions;

public class BarRecordNotFoundExceptionTests
{
    [Fact]
    public void Constructor_SetsIdAndMessage()
    {
        const int barId = 404;

        var exception = new BarRecordNotFoundException(barId);

        Assert.Equal(barId, exception.Id);
        Assert.Equal("Bar record 404 was not found.", exception.Message);
    }
}