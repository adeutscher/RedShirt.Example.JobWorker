using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;

namespace RedShirt.Example.JobWorker.UnitTests.Tests;

public class SetupLoggingTests
{
    /// <summary>
    /// Guard against a brief problem during development where the application was logging one message in both Microsoft Logging and Serilog at the same time. 
    /// </summary>
    [Fact]
    public void ConfigureLogging_ClearsDefaultProviders_AndRegistersOnlySerilog()
    {
        var builder = Host.CreateApplicationBuilder([]);

        Assert.Contains(builder.Services, d => d.ImplementationType == typeof(ConsoleLoggerProvider));
        Assert.Contains(builder.Services, d => d.ImplementationType == typeof(DebugLoggerProvider));

        Setup.ConfigureLogging(builder);

        using var host = builder.Build();
        var providers = host.Services.GetServices<ILoggerProvider>().ToList();

        Assert.Single(providers);
        
        Assert.DoesNotContain(providers, p => p is ConsoleLoggerProvider);
        Assert.DoesNotContain(providers, p => p is DebugLoggerProvider);
        Assert.Contains("Serilog", providers[0].GetType().FullName, StringComparison.Ordinal);
    }
}
