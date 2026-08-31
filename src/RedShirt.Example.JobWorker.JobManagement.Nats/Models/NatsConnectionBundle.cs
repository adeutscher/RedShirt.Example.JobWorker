using NATS.Client.Core;
using NATS.Client.JetStream;

namespace RedShirt.Example.JobWorker.JobManagement.Nats.Models;

internal sealed class NatsConnectionBundle(INatsJSContext context)
{
    public INatsJSContext Context { get; } = context;

    public INatsConnection Connection => Context.Connection;
}