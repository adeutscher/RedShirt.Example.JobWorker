namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;

/// <summary>
///     Indicates whether subscription mode is enabled.
///     Used by <see cref="InnerActiveMqConnectionFactory" /> to prevent a circular loop.
/// </summary>
internal interface IActiveMqSubscribeConfigurationService
{
    bool IsSubscription { get; }
}

internal class ActiveMqSubscribeConfigurationService(bool isSubscription) : IActiveMqSubscribeConfigurationService
{
    public bool IsSubscription => isSubscription;
}