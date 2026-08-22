using RedShirt.Example.JobWorker.Common.Models;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.Core.Services.SourceMessages;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Services.SourceMessages;

public class SourceMessageSorterTests
{
    /// <summary>
    ///     Regardless of implementation, the sorter should always return the same number of results as were put into it.
    /// </summary>
    /// <param name="numberOfMessages"></param>
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void Test_Message_Retention(int numberOfMessages)
    {
        var items = new List<IJobModel>();
        for (var i = 0; i < numberOfMessages; i++)
        {
            var job = new Mock<IJobModel>();
            var data = new Mock<IJobDataModel>();
            job.Setup(j => j.Data).Returns(data.Object);
            items.Add(job.Object);
        }

        var sorter = new SourceMessageSorter();

        var output = sorter.GetSortedListOfJobs(items.Select(i => new JobRepositoryEntry
        {
            JobModel = i,
            RawJobModel = new Mock<IRawJobModel>(MockBehavior.Strict).Object,
            LastHeartbeatTime = DateTime.UtcNow,
            State = JobState.Inactive
        }).ToList());

        Assert.Equal(numberOfMessages, output.Count);
    }
}