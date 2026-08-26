using RabbitMQ.Client.Exceptions;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Extensions;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Extensions;

public class ExceptionExtensionsTests
{
    public static TheoryData<Exception> CredentialProblemCases =>
    [
        new AuthenticationFailureException("ACCESS_REFUSED"),
        new PossibleAuthenticationFailureException("likely ACCESS_REFUSED"),
        new BrokerUnreachableException(new AuthenticationFailureException("ACCESS_REFUSED")),
        new BrokerUnreachableException(new PossibleAuthenticationFailureException("likely ACCESS_REFUSED")),
        new WorkerJobSourceException(new AuthenticationFailureException("ACCESS_REFUSED"))
        {
            IsHandled = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = true
        },
        new WorkerJobSourceException(new PossibleAuthenticationFailureException("likely ACCESS_REFUSED"))
        {
            IsHandled = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = true
        },
        new WorkerJobSourceException(
            new BrokerUnreachableException(new AuthenticationFailureException("ACCESS_REFUSED")))
        {
            IsHandled = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = true
        },
        new WorkerJobSourceException(
            new BrokerUnreachableException(new PossibleAuthenticationFailureException("likely ACCESS_REFUSED")))
        {
            IsHandled = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = true
        }
    ];

    public static TheoryData<Exception> NonCredentialProblemCases =>
    [
        new IOException("no broker"),
        new InvalidOperationException("unrelated"),
        new BrokerUnreachableException(new IOException("no broker")),
        new WorkerJobSourceException(new IOException("no broker"))
        {
            IsHandled = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        },
        new WorkerJobSourceException("no inner")
        {
            IsHandled = true,
            CouldBeTransient = false,
            CouldBeExternallySolvable = false
        }
    ];

    [Fact]
    public void IsPotentialCredentialProblem_WhenNull_ReturnsFalse()
    {
        Assert.False(((Exception?) null).IsPotentialCredentialProblem());
    }

    [Theory]
    [MemberData(nameof(CredentialProblemCases))]
    public void IsPotentialCredentialProblem_WhenAuthRelated_ReturnsTrue(Exception exception)
    {
        Assert.True(exception.IsPotentialCredentialProblem());
    }

    [Theory]
    [MemberData(nameof(NonCredentialProblemCases))]
    public void IsPotentialCredentialProblem_WhenNotAuthRelated_ReturnsFalse(Exception exception)
    {
        Assert.False(exception.IsPotentialCredentialProblem());
    }
}
