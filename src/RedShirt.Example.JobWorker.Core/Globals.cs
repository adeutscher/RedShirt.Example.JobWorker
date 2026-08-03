using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
[assembly: InternalsVisibleTo("RedShirt.Example.JobWorker.Core.UnitTests")]
[assembly: InternalsVisibleTo("RedShirt.Example.JobWorker.Core.IntegrationTests")]

namespace RedShirt.Example.JobWorker.Core;

internal static class Globals
{
    public const int AcknowledgementRetryCount = 3;
    public const int HeartbeatRetryCount = 3;
}