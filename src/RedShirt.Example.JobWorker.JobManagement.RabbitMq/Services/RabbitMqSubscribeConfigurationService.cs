namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

/// <summary>
///     Indicates whether subscription mode is enabled.
///     Used by <see cref="Factories.InnerRabbitMqConnectionFactory" /> to prevent a circular loop.
/// </summary>
internal interface IRabbitMqSubscribeConfigurationService
{
    bool IsSubscription { get; }
}

internal class RabbitMqSubscribeConfigurationService(bool isSubscription) : IRabbitMqSubscribeConfigurationService
{
    public bool IsSubscription => isSubscription;
}
