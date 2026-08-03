using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Extensions;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Extensions;

public class CoreJobResultExtensionsTests
{
    [Theory]
    [InlineData(CoreJobResult.Failure, true)]
    [InlineData(CoreJobResult.Cancelled, true)]
    [InlineData(CoreJobResult.Success, false)]
    [InlineData(CoreJobResult.Empty, false)]
    [InlineData(CoreJobResult.Parsing, false)]
    [InlineData(CoreJobResult.Broken, false)]
    public void IsRecoverableFailure_ReturnsExpected(CoreJobResult result, bool expected)
    {
        Assert.Equal(expected, result.IsRecoverableFailure());
    }

    [Theory]
    [InlineData(CoreJobResult.Success, true)]
    [InlineData(CoreJobResult.Failure, false)]
    [InlineData(CoreJobResult.Cancelled, false)]
    [InlineData(CoreJobResult.Empty, false)]
    [InlineData(CoreJobResult.Parsing, false)]
    [InlineData(CoreJobResult.Broken, false)]
    public void IsSuccessful_ReturnsExpected(CoreJobResult result, bool expected)
    {
        Assert.Equal(expected, result.IsSuccessful());
    }

    [Theory]
    [InlineData(CoreJobResult.Empty, FailureType.Empty)]
    [InlineData(CoreJobResult.Parsing, FailureType.Parsing)]
    [InlineData(CoreJobResult.Failure, FailureType.Execution)]
    [InlineData(CoreJobResult.Cancelled, FailureType.Cancelled)]
    [InlineData(CoreJobResult.Broken, FailureType.Broken)]
    public void ToFailureType_MapsNonSuccess(CoreJobResult result, FailureType expected)
    {
        Assert.Equal(expected, result.ToFailureType());
    }

    [Fact]
    public void ToFailureType_WhenSuccess_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CoreJobResult.Success.ToFailureType());
        Assert.Equal("result", exception.ParamName);
    }
}