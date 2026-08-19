using RabbitMQ.Client;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Subscribe.Models;

internal interface IChannelWrapper
{
    IChannel Channel { get; }
    Task OnRecoveryAsync(CancellationToken cancellationToken);
    void SetRecoveryCallback(Func<CancellationToken, Task> recoveryCallback);
}

public class ChannelWrapper : IChannelWrapper
{
    private Func<CancellationToken, Task>? _recoveryCallback;

    public void SetRecoveryCallback(Func<CancellationToken, Task> recoveryCallback)
    {
        _recoveryCallback = recoveryCallback;
    }

    public required IChannel Channel { get; init; }

    public async Task OnRecoveryAsync(CancellationToken cancellationToken)
    {
        if (_recoveryCallback is not null)
        {
            await _recoveryCallback.Invoke(cancellationToken);
        }
    }
}