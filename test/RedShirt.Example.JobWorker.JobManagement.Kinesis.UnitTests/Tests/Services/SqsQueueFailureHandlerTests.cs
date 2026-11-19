using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Models;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services;
using RedShirt.Example.JobWorker.JobManagement.Kinesis.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Services;

public class SqsQueueFailureHandlerTests
{
    private const string SequenceNumber = "49590338271490256608559692538361571095921575989136588898";
    private const string ShardId = "shardId-000000000002";

    private static Mock<IRawJobModel> CreateRawJobModel(string? body = null)
    {
        var mock = new Mock<IRawJobModel>(MockBehavior.Strict);
        mock.SetupGet(m => m.Body).Returns(body ?? Guid.NewGuid().ToString());
        mock.SetupGet(m => m.MessageId).Returns(SequenceNumber);
        return mock;
    }

    private static KinesisJobModel CreateKinesisJobModel(string body)
    {
        return new KinesisJobModel
        {
            ShardId = ShardId,
            MessageId = SequenceNumber,
            Body = body,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static SqsQueueFailureHandler CreateHandler(IAmazonSQS sqs, string? queueUrl = "foo")
    {
        return new SqsQueueFailureHandler(sqs, new PassthroughRetryWrapper(), Options.Create(
                new SqsQueueFailureHandler.ConfigurationModel
                {
                    QueueUrl = queueUrl!
                }),
            NullLogger<SqsQueueFailureHandler>.Instance);
    }

    private static Mock<IAmazonSQS> CreateSqs(List<SendMessageRequest> sent, params Exception?[] outcomes)
    {
        var sqs = new Mock<IAmazonSQS>(MockBehavior.Strict);
        var call = 0;
        sqs.Setup(a => a.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Returns((SendMessageRequest request, CancellationToken _) =>
            {
                sent.Add(request);
                var outcome = call < outcomes.Length ? outcomes[call] : null;
                call++;
                return outcome is null
                    ? Task.FromResult(new SendMessageResponse())
                    : Task.FromException<SendMessageResponse>(outcome);
            });
        return sqs;
    }

    private static WorkerJobSourceException SourceFailure(bool couldBeTransient)
    {
        return new WorkerJobSourceException(new InvalidMessageContentsException("rejected"))
        {
            IsHandled = true,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = false
        };
    }

    [Fact]
    public async Task SendFailure_BodyWithCharactersSqsRejects_SendsBase64WithShardId()
    {
        var sent = new List<SendMessageRequest>();
        var sqs = CreateSqs(sent);

        await CreateHandler(sqs.Object).HandleFailureAsync(CreateKinesisJobModel("bad\u0001body"),
            FailureType.Execution, null, TestContext.Current.CancellationToken);

        var request = Assert.Single(sent);
        Assert.Equal(SqsFailureMessageBuilder.Base64Encoding,
            request.MessageAttributes[SqsFailureMessageBuilder.BodyEncodingAttribute].StringValue);
        Assert.Equal(ShardId, request.MessageAttributes[SqsQueueFailureHandler.ShardIdAttribute].StringValue);
        Assert.Equal(SequenceNumber,
            request.MessageAttributes[SqsFailureMessageBuilder.SourceMessageIdAttribute].StringValue);
    }

    [Fact]
    public async Task SendFailure_NoQueueUrl()
    {
        var sqs = new Mock<IAmazonSQS>();
        var sender = CreateHandler(sqs.Object, null);

        await sender.HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution, null,
            TestContext.Current.CancellationToken);

        Assert.Empty(sqs.Invocations);
    }

    [Fact]
    public async Task SendFailure_PermanentRejection_SendsReferenceWithShardId()
    {
        var sent = new List<SendMessageRequest>();
        var sqs = CreateSqs(sent, SourceFailure(false));
        var body = "secret-payload-" + Guid.NewGuid();

        await CreateHandler(sqs.Object).HandleFailureAsync(CreateKinesisJobModel(body), FailureType.Execution,
            null, TestContext.Current.CancellationToken);

        Assert.Equal(2, sent.Count);
        var reference = sent[1];
        Assert.True(SqsFailureMessageBuilder.IsReference(reference));
        Assert.DoesNotContain(body, reference.MessageBody);
        Assert.Contains(ShardId, reference.MessageBody);
        Assert.Contains(SequenceNumber, reference.MessageBody);
        Assert.Equal(ShardId, reference.MessageAttributes[SqsQueueFailureHandler.ShardIdAttribute].StringValue);
    }

    [Fact]
    public async Task SendFailure_QueueUrl()
    {
        var sqs = new Mock<IAmazonSQS>();
        var body = Guid.NewGuid().ToString();
        var sender = CreateHandler(sqs.Object);

        await sender.HandleFailureAsync(CreateRawJobModel(body).Object, FailureType.Execution, null,
            TestContext.Current.CancellationToken);

        Assert.Single(sqs.Invocations);
        sqs.Verify(
            a => a.SendMessageAsync(
                It.Is<SendMessageRequest>(r => r.QueueUrl == "foo" && r.MessageBody == body
                                                                   && (r.MessageAttributes == null ||
                                                                       r.MessageAttributes.Count == 0)),
                TestContext.Current.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task SendFailure_ReferenceAlsoRejected_Throws()
    {
        var sent = new List<SendMessageRequest>();
        var second = SourceFailure(false);
        var sqs = CreateSqs(sent, SourceFailure(false), second);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution, null,
                TestContext.Current.CancellationToken));

        Assert.Same(second, thrown);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public async Task SendFailure_TransientFailure_ThrowsWithoutReference()
    {
        var sent = new List<SendMessageRequest>();
        var failure = SourceFailure(true);
        var sqs = CreateSqs(sent, failure);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution, null,
                TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Single(sent);
    }

    private sealed class PassthroughRetryWrapper : IKinesisRetryWrapperService
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }

        public Task RunAsync(Func<CancellationToken, Task> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }
    }
}