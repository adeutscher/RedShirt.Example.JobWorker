using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Health.Configuration;
using RedShirt.Example.JobWorker.Common.Health.Constants;
using RedShirt.Example.JobWorker.Core.Services.Health;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Services;

public sealed class HealthPagesHttpListenerService(
    ICoreHealthStateReaderService healthService,
    ICoreStatisticsService statisticsService,
    IOptions<CommonHealthConfigurationModel> options,
    ILogger<HealthPagesHttpListenerService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private HttpListener? _listener;

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case HealthPathConstants.LivePath:
                    await WritePlainTextAsync(context, 200, "OK", cancellationToken);
                    break;
                case HealthPathConstants.HealthPath:
                    var isHealthy = healthService.IsHealthy();
                    await WritePlainTextAsync(context, isHealthy ? 200 : 503, isHealthy ? "OK" : "unhealthy",
                        cancellationToken);
                    break;
                case HealthPathConstants.StatisticsPath:
                    await WriteJsonAsync(context, 200, statisticsService.GetStatistics(), cancellationToken);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling health request");

            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (Exception)
            {
                // Ignore failures while aborting a broken response.
            }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object content,
        CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content, JsonOptions));
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private static async Task WritePlainTextAsync(HttpListenerContext context, int statusCode, string body,
        CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{options.Value.Port}/");

        try
        {
            _listener.Start();
        }
#pragma warning disable S2139
        // I don't understand what Sonar's getting at raising S2139 here.
        // It's very clearly being LogErrored and then re-thrown.
        catch (Exception ex)
#pragma warning restore S2139
        {
            logger.LogError(ex, "Failed to start health HttpListener on port {Port}: {Message}", options.Value.Port,
                ex.Message);
            throw;
        }

        logger.LogInformation("Health listening on port {Port}", options.Value.Port);

        stoppingToken.Register(() =>
        {
            try
            {
                _listener?.Stop();
            }
            catch (ObjectDisposedException)
            {
                // Pass
            }
            catch (HttpListenerException)
            {
                // Pass
            }
        });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleRequestAsync(context, stoppingToken);
            }
        }
        finally
        {
            _listener.Stop();
            _listener.Close();
            _listener = null;
        }
    }
}