using Apache.NMS;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Factories;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services.Resilience;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

internal interface IActiveMqConsumerRetryWrapper
{
    Task GetChannelAndDoActionWithRetryAsync(Func<IMessageConsumer, CancellationToken, Task> callback,
        Action<IConnection>? onNewConnectionCallback = null,
        Action<IMessageConsumer>? onNewMessageConsumerCallback = null,
        CancellationToken cancellationToken = default);

    void ResetConsumer();
}

internal class ActiveMqConsumerRetryWrapper(
    IActiveMqConnectionFactory connectionFactory,
    IActiveMqRetryWrapperService retryWrapperService,
    IActiveMqExceptionArbiterService exceptionArbiterService,
    IOptions<ActiveMqConfigurationModel> configuration) : IActiveMqConsumerRetryWrapper
{
    private IMessageConsumer? _messageConsumer;

    private async Task CallbackAsync(Func<IMessageConsumer, CancellationToken, Task> callback,
        RetryState state,
        Action<IConnection>? onNewConnectionCallback,
        Action<IMessageConsumer>? onNewMessageConsumerCallback,
        CancellationToken cancellationToken)
    {
        if (state.Exception is not null)
        {
            state.RetryNumber++;
            ResetConsumer();
            // Future: Distinguish between exceptions, in the style of RabbitMQ
        }

        // ReSharper disable once ReplaceWithSingleAssignment.False
        var forceNewSecretManagerPull = false;

        // ReSharper disable once ConvertIfToOrExpression
        if (state.Exception is NMSSecurityException securityException
            && exceptionArbiterService.GetReport(securityException, state.RetryNumber) is {CouldBeTransient: true})
        {
            forceNewSecretManagerPull = true;
        }

        try
        {
            var consumer = await GetConsumerAsync(onNewConnectionCallback, onNewMessageConsumerCallback,
                forceNewSecretManagerPull, cancellationToken);
            await callback(consumer, cancellationToken);
        }
        catch (Exception e)
        {
            state.Exception = e;
            throw;
        }
    }

    /// <summary>
    ///     Get a cached consumer or get a new one from the connection factory.
    ///     Confirming that the invocation of this method should be already covered by the retry wrapper service.
    /// </summary>
    /// <param name="onNewConnectionCallback"></param>
    /// <param name="onNewMessageConsumerCallback"></param>
    /// <param name="forceNewSecretManagerPull"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="CouldNotLoadQueueException"></exception>
    private async Task<IMessageConsumer> GetConsumerAsync(Action<IConnection>? onNewConnectionCallback,
        Action<IMessageConsumer>? onNewMessageConsumerCallback, bool forceNewSecretManagerPull,
        CancellationToken cancellationToken)
    {
        if (_messageConsumer is not null)
        {
            return _messageConsumer;
        }

        var connection = await connectionFactory.GetConnectionAsync(
            forceNewSecretManagerPull,
            cancellationToken);
        onNewConnectionCallback?.Invoke(connection);
        await connection.StartAsync();
        var session = await connection.CreateSessionAsync(AcknowledgementMode.ClientAcknowledge);
        var queue = await session.GetQueueAsync(configuration.Value.QueueName);

        if (queue is null)
        {
            throw new CouldNotLoadQueueException();
        }

        var consumer = await session.CreateConsumerAsync(queue);
        onNewMessageConsumerCallback?.Invoke(consumer);
        // Cache for later
        _messageConsumer = consumer;

        return consumer;
    }

    public void ResetConsumer()
    {
        _messageConsumer = null;
    }

    public Task GetChannelAndDoActionWithRetryAsync(Func<IMessageConsumer, CancellationToken, Task> callback,
        Action<IConnection>? onNewConnectionCallback = null,
        Action<IMessageConsumer>? onNewMessageConsumerCallback = null,
        CancellationToken cancellationToken = default)
    {
        return retryWrapperService.RunAsync(
            (state, ct) => CallbackAsync(callback, state, onNewConnectionCallback, onNewMessageConsumerCallback, ct),
            new RetryState
            {
                Exception = null,
                RetryNumber = 0
            }, cancellationToken);
    }

    private sealed class RetryState
    {
        public required Exception? Exception { get; set; }
        public required int RetryNumber { get; set; }
    }
}