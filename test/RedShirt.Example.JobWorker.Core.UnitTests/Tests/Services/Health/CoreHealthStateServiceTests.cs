using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Services.Health;
using System.Reflection;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Health;

public class CoreHealthStateServiceTests
{
    private static CoreHealthStateService CreateService(
        bool enabled = true,
        int? recentIncidentThresholdSeconds = 60)
    {
        return new CoreHealthStateService(Options.Create(new CoreHealthStateService.ConfigurationModel
        {
            Enabled = enabled,
            RecentIncidentThresholdSeconds = recentIncidentThresholdSeconds
        }));
    }

    private static void SetLastIncident(CoreHealthStateService service, DateTime? lastIncident)
    {
        var field = typeof(CoreHealthStateService).GetField("_lastIncident",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(service, lastIncident);
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(15, 15)]
    public void EffectiveRecentIncidentThreshold_UsesConfiguredOrDefaultFloor(
        int? configuredSeconds,
        int expectedSeconds)
    {
        var options = new CoreHealthStateService.ConfigurationModel
        {
            RecentIncidentThresholdSeconds = configuredSeconds
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.EffectiveRecentIncidentThreshold);
    }

    [Fact]
    public void IsHealthy_WhenDisabled_ReturnsTrueEvenAfterIncident()
    {
        var service = CreateService(false);

        service.NoteIncident();

        Assert.True(service.IsHealthy());
    }

    [Fact]
    public void IsHealthy_WhenIncidentOlderThanThreshold_ReturnsTrue()
    {
        var service = CreateService(recentIncidentThresholdSeconds: 30);

        service.NoteIncident();
        SetLastIncident(service, DateTime.UtcNow.AddSeconds(-45));

        Assert.True(service.IsHealthy());
    }

    [Fact]
    public void IsHealthy_WhenNoIncident_ReturnsTrue()
    {
        var service = CreateService();

        Assert.True(service.IsHealthy());
    }

    [Fact]
    public void IsHealthy_WhenRecentIncident_ReturnsFalse()
    {
        var service = CreateService(recentIncidentThresholdSeconds: 60);

        service.NoteIncident();

        Assert.False(service.IsHealthy());
    }

    [Fact]
    public void NoteIncident_UpdatesLastIncident()
    {
        var service = CreateService(recentIncidentThresholdSeconds: 60);

        Assert.True(service.IsHealthy());

        service.NoteIncident();

        Assert.False(service.IsHealthy());
    }
}