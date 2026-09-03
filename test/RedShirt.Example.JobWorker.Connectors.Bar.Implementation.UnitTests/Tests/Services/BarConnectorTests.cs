using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Services.Utility;
using RedShirt.Example.JobWorker.Connectors.Bar.Core.Models;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Clients;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Exceptions;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Factories;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services;
using RedShirt.Example.JobWorker.Connectors.Bar.Implementation.Services.Resilience;

namespace RedShirt.Example.JobWorker.Connectors.Bar.Implementation.UnitTests.Tests.Services;

public class BarConnectorTests
{
    private static IOptions<BarConnector.ConfigurationModel> CreateOptions(int? reasonToWaitFallbackSeconds = null)
    {
        return Options.Create(new BarConnector.ConfigurationModel
        {
            ReasonToWaitFallbackSeconds = reasonToWaitFallbackSeconds
        });
    }

    private static BarConnector CreateConnector(
        Mock<IBarApiClient> apiClient,
        IList<TimeSpan>? capturedDelays = null)
    {
        var factory = new Mock<IBarApiClientFactory>(MockBehavior.Strict);
        factory.Setup(f => f.CreateBarApiClient()).Returns(apiClient.Object);

        var retryWrapper = new Mock<IBarRetryWrapperService>(MockBehavior.Strict);
        retryWrapper
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<GetBarConnectorResponse>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<GetBarConnectorResponse>>, CancellationToken>((func, token) =>
                func(token));
        retryWrapper
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<CreateBarConnectorResponse>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<CreateBarConnectorResponse>>, CancellationToken>((func, token) =>
                func(token));

        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        sleep.Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((delay, _) =>
            {
                capturedDelays?.Add(delay);
                return Task.CompletedTask;
            });

        return new BarConnector(
            factory.Object,
            retryWrapper.Object,
            sleep.Object,
            NullLogger<BarConnector>.Instance,
            CreateOptions());
    }

    [Theory]
    [InlineData(null, 15)]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    public void ConfigurationModel_EffectiveReasonToWaitFallback(int? configuredSeconds, int expectedSeconds)
    {
        var model = new BarConnector.ConfigurationModel {ReasonToWaitFallbackSeconds = configuredSeconds};

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), model.EffectiveReasonToWaitFallback);
    }

    [Fact]
    public async Task CreateAsync_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        var connector = CreateConnector(new Mock<IBarApiClient>(MockBehavior.Strict));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            connector.CreateAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByIdAsync_WhenRateLimitedThenSuccess_SleepsUsingRetryAfterAndReturnsRecord()
    {
        var attempts = 0;
        var capturedDelays = new List<TimeSpan>();
        var apiClient = new Mock<IBarApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetBarByIdAsync(429, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++attempts == 1)
                {
                    throw new BarRateLimitedException(TimeSpan.FromSeconds(2));
                }

                return Task.FromResult(new GetBarConnectorResponse {Id = 429, Name = "Bar-429"});
            });

        var connector = CreateConnector(apiClient, capturedDelays);

        var response = await connector.GetByIdAsync(429, TestContext.Current.CancellationToken);

        Assert.Equal(429, response.Id);
        Assert.Equal(2, attempts);
        Assert.Equal([TimeSpan.FromSeconds(2)], capturedDelays);
    }

    [Fact]
    public async Task GetByIdAsync_WhenReasonToWaitHasNoRetryAfter_UsesConfiguredFallback()
    {
        var attempts = 0;
        var capturedDelays = new List<TimeSpan>();
        var apiClient = new Mock<IBarApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(c => c.GetBarByIdAsync(1, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++attempts == 1)
                {
                    throw new BarTemporarilyUnavailableException();
                }

                return Task.FromResult(new GetBarConnectorResponse {Id = 1, Name = "Bar-1"});
            });

        var factory = new Mock<IBarApiClientFactory>(MockBehavior.Strict);
        factory.Setup(f => f.CreateBarApiClient()).Returns(apiClient.Object);

        var retryWrapper = new Mock<IBarRetryWrapperService>(MockBehavior.Strict);
        retryWrapper
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<GetBarConnectorResponse>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<GetBarConnectorResponse>>, CancellationToken>((func, token) =>
                func(token));

        var sleep = new Mock<ISleepService>(MockBehavior.Strict);
        sleep.Setup(s => s.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<TimeSpan, CancellationToken>((delay, _) =>
            {
                capturedDelays.Add(delay);
                return Task.CompletedTask;
            });

        var connector = new BarConnector(
            factory.Object,
            retryWrapper.Object,
            sleep.Object,
            NullLogger<BarConnector>.Instance,
            Options.Create(new BarConnector.ConfigurationModel {ReasonToWaitFallbackSeconds = 10}));

        await connector.GetByIdAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(10)], capturedDelays);
    }
}