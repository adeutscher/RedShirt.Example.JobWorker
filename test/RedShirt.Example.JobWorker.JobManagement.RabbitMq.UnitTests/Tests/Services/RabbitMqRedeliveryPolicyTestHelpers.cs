using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Configuration;
using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

internal static class RabbitMqRedeliveryPolicyTestHelpers
{
    public static RabbitMqRedeliveryPolicy Create(int? maximumDeliveries = null)
    {
        return new RabbitMqRedeliveryPolicy(
            Options.Create(new RabbitMqQueueConfigurationModel {MaximumDeliveries = maximumDeliveries}),
            NullLogger<RabbitMqRedeliveryPolicy>.Instance);
    }
}
