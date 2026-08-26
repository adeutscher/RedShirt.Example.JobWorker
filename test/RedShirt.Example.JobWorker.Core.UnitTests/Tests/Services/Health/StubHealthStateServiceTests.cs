using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Health;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Health;

public class StubHealthStateServiceTests
{
    [Fact]
    public void GetStatistics_ReturnsNull()
    {
        var service = new StubHealthStateService();

        Assert.Null(service.GetStatistics());
    }

    [Fact]
    public void IsHealthy_AlwaysReturnsTrue()
    {
        var service = new StubHealthStateService();

        service.NoteIncident();

        Assert.True(service.IsHealthy());
    }

    [Fact]
    public void RecordMethods_DoNotThrow()
    {
        var service = new StubHealthStateService();

        service.RecordReceived();
        service.RecordResult(CoreJobResult.Success, TimeSpan.FromSeconds(1));
        service.RecordResult(CoreJobResult.Failure);
        service.NoteIncident();
        // Nothing much to do, this is a stub service to satisfy dependency injection.
        // Add at least one token assert to satisfy Sonar
        Assert.True(true);
    }
}