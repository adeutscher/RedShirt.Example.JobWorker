using RedShirt.Example.JobWorker.Common.Health.Constants;
using System.Net;

namespace RedShirt.Example.JobWorker.HealthCheck;

internal static class HealthCheckApp
{
    internal readonly record struct ParsedArgs(string? BaseUrl, int? Port, bool ShowHelp);

    internal static ParsedArgs ParseArgs(string[] args)
    {
        string? baseUrl = null;
        int? port = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--base-url" when i + 1 < args.Length:
                    baseUrl = args[++i].TrimEnd('/');
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedPort):
                    port = parsedPort;
                    break;
                case "--help" or "-h":
                    showHelp = true;
                    break;
            }
        }

        return new ParsedArgs(baseUrl, port, showHelp);
    }

    internal static Uri BuildUri(string baseUrl, int port)
    {
        return new UriBuilder(baseUrl.TrimEnd('/'))
        {
            Port = port,
            Path = HealthPathConstants.HealthPath
        }.Uri;
    }

    internal static async Task<int> RunAsync(
        string[] args,
        HttpMessageHandler? handler = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        error ??= Console.Error;
        var parsed = ParseArgs(args);

        if (parsed.ShowHelp)
        {
            await WriteUsageAsync(error);
            return 0;
        }

        if (parsed.BaseUrl is null || parsed.Port is null)
        {
            await error.WriteLineAsync("Both --base-url and --port are required.");
            await WriteUsageAsync(error);
            return 1;
        }

        var uri = BuildUri(parsed.BaseUrl, parsed.Port.Value);
        using var client = handler is null
            ? new HttpClient {Timeout = TimeSpan.FromSeconds(2)}
            : new HttpClient(handler, disposeHandler: true) {Timeout = TimeSpan.FromSeconds(2)};

        try
        {
            using var response = await client.GetAsync(uri, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static async Task WriteUsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(
            "Usage: RedShirt.Example.JobWorker.HealthCheck --base-url <url> --port <port>");
        await error.WriteLineAsync(
            $"Probes {{base-url}}:{{port}}{HealthPathConstants.HealthPath} and exits 0 on HTTP 200.");
    }
}
