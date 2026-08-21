using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Configuration;

public class CoreConfigurationServiceTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void GetBacklogSize_ReturnsEffectiveBacklogSize(int configuredBacklogSize, int expectedBacklogSize)
    {
        var service = new CoreConfigurationService(
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            Options.Create(new JobRepository.ConfigurationModel {BacklogSize = configuredBacklogSize}));

        Assert.Equal(expectedBacklogSize, service.GetBacklogSize());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsHaltOnFailure_ReturnsConfiguredValue(bool haltOnFailure)
    {
        var service = new CoreConfigurationService(
            Options.Create(new CoreConfigurationModel {HaltOnFailure = haltOnFailure}),
            Options.Create(new JobRepository.ConfigurationModel {BacklogSize = 1}));

        Assert.Equal(haltOnFailure, service.IsHaltOnFailure());
    }
}