using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RedShirt.Example.JobWorker.Health;

internal static class HealthCheckResponseWriter
{
    public static Task WritePlainTextAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain";

        if (report.Status == HealthStatus.Healthy)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return context.Response.WriteAsync("ok");
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsync("not ready");
    }
}
