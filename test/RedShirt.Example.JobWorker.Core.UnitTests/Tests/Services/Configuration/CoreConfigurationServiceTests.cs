using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Services.Configuration;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Configuration;

public class CoreConfigurationServiceTests
{
    [Theory]
    [InlineData(-10, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void FetchCount_ReturnsEffectiveFetchCount(int configuredFetchCount, int expectedFetchCount)
    {
        var service = new CoreConfigurationService(
            Options.Create(new CoreConfigurationModel {HaltOnFailure = false}),
            Options.Create(new JobSourceConfigurationModel {FetchCount = configuredFetchCount}));

        Assert.Equal(expectedFetchCount, service.FetchCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsHaltOnFailure_ReturnsConfiguredValue(bool haltOnFailure)
    {
        var service = new CoreConfigurationService(
            Options.Create(new CoreConfigurationModel {HaltOnFailure = haltOnFailure}),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

        Assert.Equal(haltOnFailure, service.IsHaltOnFailure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsTreatingTransientExceptionAsFailure_ReturnsConfiguredValue(bool treatTransientExceptionAsFailure)
    {
        var service = new CoreConfigurationService(
            Options.Create(new CoreConfigurationModel
            {
                HaltOnFailure = false,
                TreatTransientExceptionAsFailure = treatTransientExceptionAsFailure
            }),
            Options.Create(new JobSourceConfigurationModel {FetchCount = 1}));

        Assert.Equal(treatTransientExceptionAsFailure, service.IsTreatingTransientExceptionAsFailure);
    }
}