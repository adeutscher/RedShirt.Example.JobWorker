using Confluent.Kafka;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.Kafka.Services;
using System.Net.Sockets;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.UnitTests.Tests.Services;

public class KafkaExceptionArbiterServiceTests
{
    private readonly KafkaExceptionArbiterService _sut = new();

    [Fact]
    public void GetReport_ArgumentException_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new ArgumentException("bad topic", "topic"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(ErrorCode.Local_Fatal)]
    [InlineData(ErrorCode.Local_Authentication)]
    [InlineData(ErrorCode.SaslAuthenticationFailed)]
    [InlineData(ErrorCode.TopicAuthorizationFailed)]
    [InlineData(ErrorCode.GroupAuthorizationFailed)]
    [InlineData(ErrorCode.ClusterAuthorizationFailed)]
    [InlineData(ErrorCode.UnsupportedVersion)]
    [InlineData(ErrorCode.InvalidRequest)]
    [InlineData(ErrorCode.Local_MaxPollExceeded)]
    public void GetReport_KafkaException_CriticalCodes_IsCriticalAndNotTransient(ErrorCode code)
    {
        var report = _sut.GetReport(new KafkaException(code));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Theory]
    [InlineData(ErrorCode.OffsetOutOfRange)]
    [InlineData(ErrorCode.UnknownTopicOrPart)]
    [InlineData(ErrorCode.IllegalGeneration)]
    [InlineData(ErrorCode.UnknownMemberId)]
    [InlineData(ErrorCode.RebalanceInProgress)]
    public void GetReport_KafkaException_PermanentCodes_IsNotCriticalAndNotTransient(ErrorCode code)
    {
        var report = _sut.GetReport(new KafkaException(code));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
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
    public void GetReport_KafkaException_TransientCodes_IsNotCriticalAndTransient(ErrorCode code)
    {
        var report = _sut.GetReport(new KafkaException(code));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_KafkaRetriableException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new KafkaRetriableException(new Error(ErrorCode.RequestTimedOut)));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_MultiInnerAggregateException_IsCritical()
    {
        var exception = new AggregateException(
            new KafkaException(ErrorCode.Local_TimedOut),
            new SocketException((int) SocketError.TimedOut));

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_NullException_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.GetReport(null!));
    }

    [Fact]
    public void GetReport_OperationCanceledException_IsNotCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new OperationCanceledException("caller cancelled"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SingleInnerAggregateException_JudgesUnwrappedInner()
    {
        var inner = new KafkaException(ErrorCode.Local_TimedOut);
        var exception = new AggregateException(inner);

        var report = _sut.GetReport(exception);

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_SocketException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new SocketException((int) SocketError.TimedOut));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TaskCanceledException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new TaskCanceledException("request timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_TimeoutException_IsNotCriticalAndTransient()
    {
        var report = _sut.GetReport(new TimeoutException("timed out"));

        Assert.False(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_UnrecognizedException_IsCriticalAndNotTransient()
    {
        var report = _sut.GetReport(new InvalidOperationException("unexpected failure"));

        Assert.False(report.AlreadyHandled);
        Assert.True(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_Handled_DoesNotRetry()
    {
        var exception = new WorkerJobSourceException("already handled", false, true,
            true);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.False(report.CouldBeTransient);
    }

    [Fact]
    public void GetReport_WorkerJobSourceException_UnhandledTransient_MayRetry()
    {
        var exception = new WorkerJobSourceException("transient", false, true);

        var report = _sut.GetReport(exception);

        Assert.True(report.AlreadyHandled);
        Assert.False(report.IsCritical);
        Assert.True(report.CouldBeTransient);
    }
}