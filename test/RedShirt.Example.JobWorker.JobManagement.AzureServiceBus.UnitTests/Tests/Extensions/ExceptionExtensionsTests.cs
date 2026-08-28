using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.Extensions;

namespace RedShirt.Example.JobWorker.JobManagement.AzureServiceBus.UnitTests.Tests.Extensions;

public class ExceptionExtensionsTests
{
    [Fact]
    public void IsPotentialCredentialProblem_WhenUnauthorized_ReturnsTrue()
    {
        Assert.True(new UnauthorizedAccessException().IsPotentialCredentialProblem());
        Assert.True(new WorkerJobSourceException(new UnauthorizedAccessException())
        {
            IsHandled = false,
            CouldBeTransient = false,
            CouldBeExternallySolvable = true
        }.IsPotentialCredentialProblem());
    }

    [Fact]
    public void IsPotentialCredentialProblem_WhenNotAuthRelated_ReturnsFalse()
    {
        Assert.False(new Exception("mystery").IsPotentialCredentialProblem());
        Assert.False(((Exception?) null).IsPotentialCredentialProblem());
    }
}
