using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Common.Health.Configuration;
using RedShirt.Example.JobWorker.Extensions;
using RedShirt.Example.JobWorker.Services;
using Serilog;

namespace RedShirt.Example.JobWorker;

public static class Setup
{
    private static void ConfigureWorkerServices(IServiceCollection services, ConfigurationManager configuration)
    {
        services
            .AddOptions()
            .ConfigureWorker(configuration)
            .AddHostedService<JobWorkerHostedService>();
    }

    /// <summary>
    ///     Configures Serilog as the sole <see cref="ILoggerProvider" /> for
    ///     <paramref name="builder" />, clearing providers registered by
    ///     <see cref="Host.CreateApplicationBuilder(string[])" />.
    /// </summary>
    internal static void ConfigureLogging(HostApplicationBuilder builder)
    {
        if (!Enum.TryParse<LogLevel>(builder.Configuration["LogLevel"], out var logLevel))
        {
            logLevel = LogLevel.Warning;
        }

        // Configure Serilog before wiring it as the sole Microsoft.Extensions.Logging provider.
        Log.Logger = new LoggerConfiguration()
            // Need to set a minimum log level in both Serilog-land and Microsoft-land
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "{Level:u3} {Message:l}{NewLine}{Exception}")
            .CreateLogger();

        // Host.CreateApplicationBuilder registers Console/Debug providers by default;
        // clear them so messages are not emitted twice (Microsoft.Extensions.Logging format + Serilog format).
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);
        builder.Logging.SetMinimumLevel(logLevel);
    }

    public static IHost GetHost(string[]? args = null)
    {
        var builder = Host.CreateApplicationBuilder(args ?? []);
        builder.Configuration.AddEnvironmentVariablesWithSegmentSupport();

        ConfigureLogging(builder);
        ConfigureWorkerServices(builder.Services, builder.Configuration);

        builder.Services
            .Configure<CommonHealthConfigurationModel>(
                builder.Configuration.GetSection(CommonHealthConfigurationModel.SectionName))
            .AddHostedService<HealthPagesHttpListenerService>();

        return builder.Build();
    }
}