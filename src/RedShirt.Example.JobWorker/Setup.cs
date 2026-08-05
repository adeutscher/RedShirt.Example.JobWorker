using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Configuration;
using RedShirt.Example.JobWorker.Extensions;
using RedShirt.Example.JobWorker.Services;
using Serilog;

namespace RedShirt.Example.JobWorker;

public static class Setup
{
    public static async Task RunAsync(string[]? args = null)
    {
        ConfigureSerilog();

        var builder = Host.CreateApplicationBuilder(args ?? []);
        builder.Configuration.AddEnvironmentVariablesWithSegmentSupport();

        ConfigureCommonServices(builder.Services, (IConfigurationRoot)builder.Configuration);
        builder.Services
            .Configure<HealthOptions>(builder.Configuration.GetSection(HealthOptions.SectionName))
            .AddHostedService<HealthZPagesHttpListenerService>();

        await builder.Build().RunAsync();
    }

    private static void ConfigureSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            // Need to set a minimum log level in both Serilog-land and Microsoft-land
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "{Level:u3} {Message:l}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void ConfigureCommonServices(IServiceCollection services, IConfigurationRoot configuration)
    {
        if (!Enum.TryParse<LogLevel>(configuration["LogLevel"], out var logLevel))
        {
            logLevel = LogLevel.Warning;
        }

        services
            .AddLogging(loggingBuilder =>
                loggingBuilder
                    .AddSerilog(dispose: true)
                    .SetMinimumLevel(logLevel))
            .AddOptions()
            .ConfigureWorker(configuration)
            .AddHostedService<JobWorkerHostedService>();
    }
}
