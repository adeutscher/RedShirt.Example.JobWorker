using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Extensions;

public class FailureTypeExtensionsTests
{
    [Theory]
    [InlineData(FailureType.Execution, true)]
    [InlineData(FailureType.Cancelled, true)]
    [InlineData(FailureType.Empty, false)]
    [InlineData(FailureType.Parsing, false)]
    [InlineData(FailureType.Broken, false)]
    public void IsRecoverable_ReturnsExpected(FailureType failureType, bool expected)
    {
        Assert.Equal(expected, failureType.IsRecoverable());
    }
}