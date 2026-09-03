using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Clients;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Factories;

internal interface IBarApiClientFactory
{
    IBarApiClient CreateBarApiClient();
}

internal sealed class BarApiClientFactory(
    IHttpClientFactory httpClientFactory,
    IOptions<BarApiClientFactory.ConfigurationModel> configuration) : IBarApiClientFactory
{
    public IBarApiClient CreateBarApiClient()
    {
        var httpClient = httpClientFactory.CreateClient(nameof(BarApiClient));
        return new BarApiClient(httpClient, configuration.Value.BaseUrl);
    }

    internal sealed class ConfigurationModel
    {
        public required string BaseUrl { get; init; }
    }
}