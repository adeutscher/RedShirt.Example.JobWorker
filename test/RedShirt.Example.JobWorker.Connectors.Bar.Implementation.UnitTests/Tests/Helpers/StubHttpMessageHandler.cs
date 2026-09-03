namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.UnitTests.Tests.Helpers;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public IList<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }
}