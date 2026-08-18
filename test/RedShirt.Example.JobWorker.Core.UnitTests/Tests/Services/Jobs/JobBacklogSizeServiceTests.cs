using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Services.Jobs;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.Jobs;

public class JobBacklogSizeServiceTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void BacklogSize_ReturnsEffectiveBacklogSize(int configuredBacklogSize, int expectedBacklogSize)
    {
        var service = new JobBacklogSizeService(Options.Create(new JobRepository.ConfigurationModel
        {
            BacklogSize = configuredBacklogSize
        }));

        Assert.Equal(expectedBacklogSize, service.BacklogSize);
    }
}
