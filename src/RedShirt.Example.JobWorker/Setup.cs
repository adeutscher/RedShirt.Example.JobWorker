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
            // Need to set a minimum log level in both Serilog-land and Microsoft-land
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "{Level:u3} {Message:l}{NewLine}{Exception}")
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