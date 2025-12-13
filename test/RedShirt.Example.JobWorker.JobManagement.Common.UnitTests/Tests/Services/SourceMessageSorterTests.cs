using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Common.Services;

namespace RedShirt.Example.JobWorker.JobManagement.Common.UnitTests.Tests.Services;

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
            items.Add(new Mock<IJobModel>().Object);
        }

        var sorter = new SourceMessageSorter();

        var output = sorter.GetSortedListOfJobs(items);

        Assert.Equal(numberOfMessages, output.Count);
    }
}