using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;

namespace RedShirt.Example.JobWorker.UnitTests.Tests;

public class SetupTests
{
    /// <summary>
    ///     Guard against a brief problem during development where the application was logging one message in both Microsoft
    ///     Logging and Serilog at the same time.
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

    [Fact]
    public void ConfigureLogging_WhenAppLogLevelIsTrace_StillSuppressesHostingLifetimeInformation()
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LogLevel"] = "Trace"
        });

        Setup.ConfigureLogging(builder);

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Microsoft.Hosting.Lifetime");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }
}