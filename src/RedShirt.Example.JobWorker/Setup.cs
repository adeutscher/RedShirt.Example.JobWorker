using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Health.Configuration;
using RedShirt.Example.JobWorker.Configuration;
using RedShirt.Example.JobWorker.Extensions;
using RedShirt.Example.JobWorker.Services;
using Serilog;

namespace RedShirt.Example.JobWorker;

public static class Setup
{
    public static IHost GetHost(string[]? args = null)
    {
        var builder = Host.CreateApplicationBuilder(args ?? []);
        builder.Configuration.AddEnvironmentVariablesWithSegmentSupport();

        ConfigureLogging(builder.Services, builder.Configuration);
        ConfigureWorkerServices(builder.Services, builder.Configuration);

        builder.Services
            .Configure<CommonHealthConfigurationModel>(
                builder.Configuration.GetSection(CommonHealthConfigurationModel.SectionName))
            .Configure<HealthConfigurationModel>(
                builder.Configuration.GetSection(CommonHealthConfigurationModel.SectionName))
            .AddHostedService<HealthZPagesHttpListenerService>();

        return builder.Build();
    }

    private static void ConfigureLogging(IServiceCollection services, ConfigurationManager configuration)
    {
        /* General Logging */

        if (!Enum.TryParse<LogLevel>(configuration["LogLevel"], out var logLevel))
        {
            logLevel = LogLevel.Warning;
        }

        services
            .AddLogging(loggingBuilder =>
                loggingBuilder
                    .AddSerilog(dispose: true)
                    .SetMinimumLevel(logLevel));
        
        /* Configure Serilog */
        Log.Logger = new LoggerConfiguration()
            // Need to set a minimum log level in both Serilog-land and Microsoft-land
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "{Level:u3} {Message:l}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void ConfigureWorkerServices(IServiceCollection services, ConfigurationManager configuration)
    {
        services
            .AddOptions()
            .ConfigureWorker(configuration)
            .AddHostedService<JobWorkerHostedService>();
    }
}
