using RedShirt.Example.JobWorker.Common.Health.Models;
using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Services.Health;

/// <summary>
///     Stub implementation of health services to not waste any resources on things like statistics tallying.
///     Yes, it's a bit silly.
/// </summary>
internal class StubHealthStateService : ICoreHealthStateReaderService, ICoreHealthStateUpdateService,
    ICoreStatisticsService
{
    public bool IsHealthy()
    {
        // Pass
        return true;
    }

    public void NoteIncident()
    {
        // Pass
    }

    public StatisticsModel GetStatistics()
    {
        // Pass
        return null!;
    }

    public void RecordReceived()
    {
        // Pass
    }

    public void RecordResult(CoreJobResult result, TimeSpan duration = default)
    {
        // Pass
    }
}