using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Core.Extensions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Extensions;

public class JobModelExtensionTests
{
    /// <summary>
    ///     Confirm HoursOld, especially for values >=24 hours
    ///     Made to pin down a cur
    /// </summary>
    /// <param name="hoursOld"></param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(48)]
    public void TestHoursOld(int hoursOld)
    {
        var jobModel = new Mock<IJobModel>(MockBehavior.Strict);
        jobModel.Setup(j => j.CreatedAtUtc).Returns(DateTime.UtcNow - TimeSpan.FromHours(hoursOld));

        Assert.Equal(hoursOld, jobModel.Object.HoursOld);
    }
}