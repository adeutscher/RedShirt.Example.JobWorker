using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Extensions;
using Serilog;

namespace RedShirt.Example.JobWorker;

public static class Setup
{
    public static Runner GetRunner()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariablesWithSegmentSupport()
            .Build();

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        if (!Enum.TryParse<LogLevel>(configuration["LogLevel"], out var logLevel))
        {
            logLevel = LogLevel.Warning;
        }

        var provider = new ServiceCollection()
            .AddLogging(loggingBuilder =>
                loggingBuilder
                    .AddSerilog(dispose: true)
                    .SetMinimumLevel(logLevel))
            .AddOptions()
            .AddSingleton<Runner>()
            .ConfigureWorker(configuration)
            .BuildServiceProvider();

        return provider.GetRequiredService<Runner>();
    }
}