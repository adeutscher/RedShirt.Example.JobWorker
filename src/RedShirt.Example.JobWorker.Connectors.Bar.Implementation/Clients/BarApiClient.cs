using RedShirt.Example.JobWorker.Connectors.Bar.Core.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Models;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Models.Requests;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Models.Responses;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Clients;

internal interface IBarApiClient
{
    Task<CreateBarConnectorResponse> CreateBarAsync(CreateBarConnectorRequest request,
        CancellationToken cancellationToken = default);

    Task<GetBarConnectorResponse> GetBarByIdAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
///     HTTP transport for the Bar dependency. Failures surface as raw framework exceptions
///     (<see cref="HttpRequestException" />, <see cref="JsonException" />, timeouts, etc.),
///     except get-by-id HTTP 404 which surfaces as <see cref="BarRecordNotFoundException" />
///     and HTTP 429 which surfaces as <see cref="BarRateLimitedException" />.
/// </summary>
internal sealed class BarApiClient(HttpClient httpClient, string baseUrl) : IBarApiClient
{
    private static void EnsureSuccessOrThrow(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new BarRateLimitedException(ParseRetryAfter(response));
        }

        throw new HttpRequestException(
            $"Response status code does not indicate success: {(int) response.StatusCode} ({response.StatusCode}).",
            null,
            response.StatusCode);
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var headerValue = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        if (int.TryParse(headerValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(headerValue, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var retryAt))
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    public async Task<CreateBarConnectorResponse> CreateBarAsync(CreateBarConnectorRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{baseUrl.TrimEnd('/')}/api/bar"));
        message.Content = new StringContent(JsonSerializer.Serialize(new InternalBarCreateRequest
        {
            Name = request.Name
        }), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        EnsureSuccessOrThrow(response);

        var stringResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObject = JsonSerializer.Deserialize<InternalBarCreateResponse>(stringResponse);
        if (responseObject is null)
        {
            throw new JsonException("Bar API create response body deserialized to null.");
        }

        return new CreateBarConnectorResponse
        {
            Id = responseObject.Id,
            Name = responseObject.Name
        };
    }

    public async Task<GetBarConnectorResponse> GetBarByIdAsync(int id,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"{baseUrl.TrimEnd('/')}/api/bar/{id}"));

        using var response = await httpClient.SendAsync(message, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BarRecordNotFoundException(id);
        }

        EnsureSuccessOrThrow(response);

        var stringResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObject = JsonSerializer.Deserialize<InternalBarGetResponse>(stringResponse);
        if (responseObject is null)
        {
            throw new JsonException("Bar API get response body deserialized to null.");
        }

        return new GetBarConnectorResponse
        {
            Id = responseObject.Id,
            Name = responseObject.Name
        };
    }
}