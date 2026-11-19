using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Exceptions;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Services.Resilience;
using RedShirt.Example.JobWorker.Common.Aws.Sqs.Utility;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Exceptions;
using RedShirt.Example.JobWorker.Core.Models;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services;
using RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.Services.Resilience;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.Kafka.FailureHandling.Sqs.UnitTests.Tests.Services;

public class SqsQueueFailureHandlerTests
{
    private const string MessageId = "jobs:3:42";

    private static Mock<IRawJobModel> CreateRawJobModel(string? body = null)
    {
        var mock = new Mock<IRawJobModel>(MockBehavior.Strict);
        mock.SetupGet(m => m.Body).Returns(body ?? Guid.NewGuid().ToString());
        mock.SetupGet(m => m.MessageId).Returns(MessageId);
        return mock;
    }

    private static SqsQueueFailureHandler CreateHandler(IAmazonSQS sqs, string? queueUrl = "foo")
    {
        // Use the real failure queue wrapper over a pass-through SQS wrapper, so exception translation is exercised.
        return new SqsQueueFailureHandler(sqs, new KafkaFailureQueueRetryWrapperService(new PassthroughRetryWrapper()),
            Options.Create(
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

    private static WorkerSqsException SqsFailure(bool couldBeTransient)
    {
        return new WorkerSqsException(new InvalidMessageContentsException("rejected"))
        {
            IsHandled = true,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = false
        };
    }

    [Fact]
    public async Task SendFailure_BodyWithCharactersSqsRejects_SendsBase64()
    {
        var sent = new List<SendMessageRequest>();
        var sqs = CreateSqs(sent);
        var body = "before\u0000after";

        await CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel(body).Object, FailureType.Execution,
            null, TestContext.Current.CancellationToken);

        var request = Assert.Single(sent);
        Assert.Equal(body, Encoding.UTF8.GetString(Convert.FromBase64String(request.MessageBody)));
        Assert.Equal(SqsFailureMessageBuilder.Base64Encoding,
            request.MessageAttributes[SqsFailureMessageBuilder.BodyEncodingAttribute].StringValue);
        Assert.Equal(MessageId,
            request.MessageAttributes[SqsFailureMessageBuilder.SourceMessageIdAttribute].StringValue);
    }

    [Fact]
    public async Task SendFailure_EmptyBody_SendsReferenceOnce()
    {
        var sent = new List<SendMessageRequest>();
        var sqs = CreateSqs(sent);

        await CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel(string.Empty).Object,
            FailureType.Empty, null, TestContext.Current.CancellationToken);

        var request = Assert.Single(sent);
        Assert.True(SqsFailureMessageBuilder.IsReference(request));
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
    public async Task SendFailure_PermanentRejection_SendsReferenceWithoutBody()
    {
        var sent = new List<SendMessageRequest>();
        var sqs = CreateSqs(sent, SqsFailure(false));
        var body = "secret-payload-" + Guid.NewGuid();

        await CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel(body).Object, FailureType.Execution,
            null, TestContext.Current.CancellationToken);

        Assert.Equal(2, sent.Count);
        Assert.Equal(body, sent[0].MessageBody);
        var reference = sent[1];
        Assert.True(SqsFailureMessageBuilder.IsReference(reference));
        Assert.DoesNotContain(body, reference.MessageBody);
        Assert.Contains(MessageId, reference.MessageBody);
        Assert.Equal(MessageId,
            reference.MessageAttributes[SqsFailureMessageBuilder.SourceMessageIdAttribute].StringValue);
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
        var second = SqsFailure(false);
        var sqs = CreateSqs(sent, SqsFailure(false), second);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution, null,
                TestContext.Current.CancellationToken));

        Assert.Same(second, thrown.InnerException);
        Assert.Equal(2, sent.Count);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task SendFailure_SqsFailure_RethrownAsJobSourceExceptionWithSameFlags(bool isHandled,
        bool couldBeTransient, bool couldBeExternallySolvable)
    {
        var sent = new List<SendMessageRequest>();
        // The empty body goes straight to a reference, so the first failure is the one that surfaces.
        var failure = new WorkerSqsException(new QueueDoesNotExistException("missing"))
        {
            IsHandled = isHandled,
            CouldBeTransient = couldBeTransient,
            CouldBeExternallySolvable = couldBeExternallySolvable
        };
        var sqs = CreateSqs(sent, failure);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel(string.Empty).Object, FailureType.Empty,
                null, TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown.InnerException);
        Assert.Equal(isHandled, thrown.IsHandled);
        Assert.Equal(couldBeTransient, thrown.CouldBeTransient);
        Assert.Equal(couldBeExternallySolvable, thrown.CouldBeExternallySolvable);
        Assert.Single(sent);
    }

    [Fact]
    public async Task SendFailure_TransientFailure_ThrowsWithoutReference()
    {
        var sent = new List<SendMessageRequest>();
        var failure = SqsFailure(true);
        var sqs = CreateSqs(sent, failure);

        var thrown = await Assert.ThrowsAsync<WorkerJobSourceException>(() =>
            CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution, null,
                TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown.InnerException);
        Assert.True(thrown.CouldBeTransient);
        Assert.Single(sent);
    }

    [Fact]
    public async Task SendFailure_UnclassifiedFailureOnReference_StaysRaw()
    {
        var sent = new List<SendMessageRequest>();
        var unexpected = new InvalidOperationException("unexpected");
        var sqs = CreateSqs(sent, SqsFailure(false), unexpected);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution, null,
                TestContext.Current.CancellationToken));

        Assert.Same(unexpected, thrown);
    }

    [Fact]
    public async Task SendFailure_UnclassifiedRejection_SendsReference()
    {
        var sent = new List<SendMessageRequest>();
        var sqs = CreateSqs(sent, new AmazonSQSException("Message must be shorter than 262144 bytes."));

        await CreateHandler(sqs.Object).HandleFailureAsync(CreateRawJobModel().Object, FailureType.Execution,
            null, TestContext.Current.CancellationToken);

        Assert.Equal(2, sent.Count);
        Assert.True(SqsFailureMessageBuilder.IsReference(sent[1]));
    }

    private sealed class PassthroughRetryWrapper : ISqsRetryWrapperService
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