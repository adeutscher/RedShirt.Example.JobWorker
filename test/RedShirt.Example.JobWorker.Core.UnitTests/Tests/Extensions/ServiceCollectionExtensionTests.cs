using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Services;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.MessagePolling;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionTests
{
    [Theory]
    [InlineData(null, typeof(BatchModeJobLoader))]
    [InlineData("0", typeof(BatchModeJobLoader))]
    [InlineData("false", typeof(BatchModeJobLoader))]
    [InlineData("1", typeof(LoaderModeJobLoader))]
    [InlineData("true", typeof(LoaderModeJobLoader))]
    public void AddCoreJobManagement_ConfiguresJobLoaderFromUseLoaderMode(string? useLoaderMode,
        Type expectedLoaderType)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:UseLoaderMode"] = useLoaderMode
            })
            .Build();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IJobLoader));
        Assert.Equal(expectedLoaderType, descriptor.ImplementationType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void AddCoreJobManagement_ConfiguresJobRepository(int backlogSize)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:BacklogSize"] = backlogSize.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JobRepository.ConfigurationModel>>().Value;
        Assert.Equal(backlogSize, options.BacklogSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void AddCoreJobManagement_ConfiguresJobSource(int batchSize)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobSource:BatchSize"] = batchSize.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JobSourceConfigurationModel>>().Value;
        Assert.Equal(batchSize, options.BatchSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(60)]
    public void AddCoreJobManagement_ConfiguresLoopOptions(int maxIdleWaitSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:MaxIdleWaitSeconds"] = maxIdleWaitSeconds.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<LoopOptionsConfigurationModel>>().Value;
        Assert.Equal(maxIdleWaitSeconds, options.MaxIdleWaitSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void AddCoreJobManagement_ConfiguresSafeJobRunner(int internalRetryCount)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:InternalRetryCount"] = internalRetryCount.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SafeJobRunner.ConfigurationModel>>().Value;
        Assert.Equal(internalRetryCount, options.InternalRetryCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void AddCoreJobManagement_ConfiguresThreadConfiguration(int workerThreadCount)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:WorkerThreadCount"] = workerThreadCount.ToString()
            })
            .Build();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ThreadConfigurationModel>>().Value;
        Assert.Equal(workerThreadCount, options.WorkerThreadCount);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("not-a-value", false)]
    [InlineData("1", true)]
    [InlineData("2", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    public void EffectiveUseLoaderModeSetting(string? useLoaderMode, bool expected)
    {
        var model = new ServiceCollectionExtensions.CoreServiceConfigurationModel
        {
            UseLoaderMode = useLoaderMode
        };

        Assert.Equal(expected, model.EffectiveUseLoaderModeSetting);
    }
}