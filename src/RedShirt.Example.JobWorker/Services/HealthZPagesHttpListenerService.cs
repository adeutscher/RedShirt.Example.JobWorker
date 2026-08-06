using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Health.Constants;
using RedShirt.Example.JobWorker.Configuration;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Services;

public sealed class HealthZPagesHttpListenerService(
    IOptions<HealthConfigurationModel> options,
    ILogger<HealthZPagesHttpListenerService> logger) : BackgroundService
{
    private HttpListener? _listener;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start health HttpListener on port {Port}", options.Value.Port);
            throw;
        }

        logger.LogInformation("Health z-pages listening on port {Port}", options.Value.Port);

        cancellationToken.Register(() =>
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
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleRequestAsync(context);
            }
        }
        finally
        {
            _listener.Stop();
            _listener.Close();
            _listener = null;
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case HealthPathConstants.LivePath:
                case HealthPathConstants.HealthPath:
                    await WritePlainTextAsync(context, 200, "OK");
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

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object content)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content));
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
    
    private static async Task WritePlainTextAsync(HttpListenerContext context, int statusCode, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}
