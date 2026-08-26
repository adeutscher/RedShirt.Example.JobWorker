using Confluent.Kafka;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services.Resilience;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services.Resilience;

public class KafkaExceptionArbiterServiceTests
{
    private readonly KafkaExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsExpectedAndNotTransient()
    {
#pragma warning disable S3928
        // ReSharper disable once NotResolvedInText
        var report = _sut.GetReport(new ArgumentException("bad topic", "topic"));
#pragma warning restore S3928

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(ErrorCode.Local_Fatal, false)]
    [InlineData(ErrorCode.Local_Authentication, true)]
    [InlineData(ErrorCode.SaslAuthenticationFailed, true)]
    [InlineData(ErrorCode.TopicAuthorizationFailed, true)]
    [InlineData(ErrorCode.GroupAuthorizationFailed, true)]
    [InlineData(ErrorCode.ClusterAuthorizationFailed, true)]
    [InlineData(ErrorCode.UnsupportedVersion, false)]
    [InlineData(ErrorCode.InvalidRequest, false)]
    [InlineData(ErrorCode.Local_MaxPollExceeded, false)]
    public void GetReport_KafkaException_CriticalCodes_IsExpectedAndNotTransient(
        ErrorCode code,
        bool couldBeExternallySolvable)
    {
        var report = _sut.GetReport(new KafkaException(code));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(ErrorCode.OffsetOutOfRange)]
    [InlineData(ErrorCode.UnknownTopicOrPart)]
    [InlineData(ErrorCode.IllegalGeneration)]
    [InlineData(ErrorCode.UnknownMemberId)]
    [InlineData(ErrorCode.RebalanceInProgress)]
    public void GetReport_KafkaException_PermanentCodes_IsExpectedAndNotTransient(ErrorCode code)
    {
        var report = _sut.GetReport(new KafkaException(code));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Theory]
    [InlineData(ErrorCode.Local_TimedOut)]
    [InlineData(ErrorCode.Local_Transport)]
    [InlineData(ErrorCode.Local_AllBrokersDown)]
    [InlineData(ErrorCode.RequestTimedOut)]
    [InlineData(ErrorCode.NetworkException)]
    [InlineData(ErrorCode.LeaderNotAvailable)]
    [InlineData(ErrorCode.NotLeaderForPartition)]
    [InlineData(ErrorCode.GroupCoordinatorNotAvailable)]
    public void GetReport_KafkaException_TransientCodes_IsExpectedAndTransient(ErrorCode code)
    {
        var report = _sut.GetReport(new KafkaException(code));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_KafkaRetriableException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new KafkaRetriableException(new Error(ErrorCode.RequestTimedOut)));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsNotExpected()
    {
        var exception = new AggregateException(
            new KafkaException(ErrorCode.Local_TimedOut),
            new SocketException((int) SocketError.TimedOut));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_OperationCanceledException_IsExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new OperationCanceledException("caller cancelled"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new KafkaException(ErrorCode.Local_TimedOut);
        var exception = new AggregateException(inner);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_SocketException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new SocketException((int) SocketError.TimedOut));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TaskCanceledException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException("request timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_TimeoutException_IsExpectedAndTransient()
    {
        var report = _sut.GetReport(new TimeoutException("timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsNotExpectedAndNotTransient()
    {
        var report = _sut.GetReport(new InvalidOperationException("unexpected failure"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.False(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_Handled_DoesNotRetry()
    {
        var exception = new WorkerJobSourceException("already handled")
        {
            IsHandled = true,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.False(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_UnhandledTransient_MayRetry()
    {
        var exception = new WorkerJobSourceException("transient")
        {
            IsHandled = false,
            CouldBeTransient = true,
            CouldBeExternallySolvable = true
        };

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.True(report.IsExpected);
        Assert.True(report.CouldBeTransient);
        Assert.True(report.CouldBeExternallySolvable);
    }
}