using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.Models;
using RedShirt.Example.JobWorker.Common.Azure.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.UnitTests.Tests.Services;

public class AzureExceptionPackagerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pack_ThrowsAzureExceptionWrapper_UsingArbiterTransientJudgement(bool isTransient)
    {
        var inner = new InvalidOperationException("azure call failed");
        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter
            .Setup(a => a.GetJudgement(inner))
            .Returns(new AzureExceptionArbiterReport
            {
                IsExpected = true,
                IsTransient = isTransient
            });

        var packager = new AzureExceptionPackager(arbiter.Object);

        var wrapped = Assert.Throws<AzureExceptionWrapper>(() => packager.Pack(inner));

        Assert.Same(inner, wrapped.InnerException);
        Assert.Equal(inner.Message, wrapped.Message);
        Assert.Equal(isTransient, wrapped.IsTransient);
        arbiter.Verify(a => a.GetJudgement(inner), Times.Once);
        arbiter.VerifyNoOtherCalls();
    }

    [Fact]
    public void Pack_WhenJudgementMarksUnexpected_StillThrowsWrapperWithTransientFlag()
    {
        var inner = new NotSupportedException("not an azure failure");
        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter
            .Setup(a => a.GetJudgement(inner))
            .Returns(new AzureExceptionArbiterReport
            {
                IsExpected = false,
                IsTransient = false
            });

        var packager = new AzureExceptionPackager(arbiter.Object);

        var wrapped = Assert.Throws<AzureExceptionWrapper>(() => packager.Pack(inner));

        Assert.Same(inner, wrapped.InnerException);
        Assert.False(wrapped.IsTransient);
        arbiter.Verify(a => a.GetJudgement(inner), Times.Once);
    }

    [Fact]
    public void Pack_PassesExactExceptionInstanceToArbiter()
    {
        var first = new HttpRequestException("first");
        var second = new HttpRequestException("second");
        var seen = new List<Exception>();

        var arbiter = new Mock<IAzureExceptionArbiterService>(MockBehavior.Strict);
        arbiter
            .Setup(a => a.GetJudgement(It.IsAny<Exception>()))
            .Callback<Exception>(seen.Add)
            .Returns(new AzureExceptionArbiterReport
            {
                IsExpected = true,
                IsTransient = true
            });

        var packager = new AzureExceptionPackager(arbiter.Object);

        Assert.Throws<AzureExceptionWrapper>(() => packager.Pack(first));
        Assert.Throws<AzureExceptionWrapper>(() => packager.Pack(second));

        Assert.Equal(2, seen.Count);
        Assert.Same(first, seen[0]);
        Assert.Same(second, seen[1]);
    }
}
