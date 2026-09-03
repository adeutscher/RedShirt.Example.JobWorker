using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;
using System.Net;
using System.Net.Http.Headers;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Clients;

/// <summary>
///     Attaches a Bar OAuth bearer token to outbound requests.
///     On <see cref="HttpStatusCode.Unauthorized" />, signals <see cref="BarUnauthorizedException" />
///     so the request handler retry wrapper can refresh the token (then credentials) and retry.
/// </summary>
internal sealed class BarApiClientHandler(
    IBarApiRequestHandlerRetryWrapperService apiRequestRetryWrapperService) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await apiRequestRetryWrapperService.ExecuteAsync(async ct =>
        {
            var accessToken = await apiRequestRetryWrapperService.GetAccessTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await base.SendAsync(request, ct);

            // ReSharper disable once InvertIf
            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new BarUnauthorizedException();
            }

            return response;
        }, cancellationToken);
    }
}