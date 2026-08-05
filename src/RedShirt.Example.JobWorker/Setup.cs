using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Configuration;
using RedShirt.Example.JobWorker.Extensions;
using RedShirt.Example.JobWorker.Health;
using RedShirt.Example.JobWorker.Services;
using Serilog;

namespace RedShirt.Example.JobWorker;

public static class Setup
{
    public static async Task RunAsync(string[]? args = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariablesWithSegmentSupport()
            .Build();

        ConfigureSerilog();

        var healthEnabled = configuration.GetValue($"{HealthOptions.SectionName}:Enabled", true);
        var healthPort = configuration.GetValue($"{HealthOptions.SectionName}:Port", 8080);

        if (healthEnabled)
        {
            await RunWithHealthEndpointsAsync(healthPort, args);
        }
        else
        {
            await RunWorkerOnlyAsync(args);
        }
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

    private static async Task RunWorkerOnlyAsync(string[]? args)
    {
        var builder = Host.CreateApplicationBuilder(args ?? []);
        builder.Configuration.AddEnvironmentVariablesWithSegmentSupport();
        ConfigureCommonServices(builder.Services, builder.Configuration);
        await builder.Build().RunAsync();
    }

    private static async Task RunWithHealthEndpointsAsync(int port, string[]? args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],
        });

        builder.Configuration.AddEnvironmentVariablesWithSegmentSupport();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

        ConfigureCommonServices(builder.Services, builder.Configuration);
        builder.Services
            .AddHealthChecks()
            .AddCheck<WorkerReadyHealthCheck>("worker_ready");

        var app = builder.Build();

        app.MapGet("/livez", () => Results.Text("ok", "text/plain"));
        app.MapGet("/healthz", () => Results.Text("ok", "text/plain"));
        app.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            ResponseWriter = HealthCheckResponseWriter.WritePlainTextAsync,
        });

        await app.RunAsync();
    }
}
