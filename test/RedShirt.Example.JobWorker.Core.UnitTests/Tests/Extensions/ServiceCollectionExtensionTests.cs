using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Health.Configuration;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Extensions;
using RedShirt.Example.JobWorker.Core.Services.Health;
using RedShirt.Example.JobWorker.Core.Services.Jobs;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Polling;
using RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;
using RedShirt.Example.JobWorker.Core.Services.Safety;

namespace RedShirt.Example.JobWorker.Core.UnitTests.Tests.Extensions;

public class ServiceCollectionExtensionTests
{
    private static IConfigurationRoot CreateConfiguration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? [])
            .Build();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AddCoreJobManagement_ConfiguresCoreConfiguration(bool haltOnFailure,
        bool treatTransientExceptionAsFailure)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:HaltOnFailure"] = haltOnFailure.ToString(),
            ["Jobs:TreatTransientExceptionAsFailure"] = treatTransientExceptionAsFailure.ToString()
        });

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<CoreConfigurationModel>>().Value;
        Assert.Equal(haltOnFailure, options.HaltOnFailure);
        Assert.Equal(treatTransientExceptionAsFailure, options.TreatTransientExceptionAsFailure);
    }

    [Fact]
    public void AddCoreJobManagement_ConfiguresIdempotency()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:Idempotency:Enabled"] = "true",
            ["Jobs:Idempotency:ResultCacheDurationSeconds"] = "45",
            ["Jobs:Idempotency:MonitorIntervalSeconds"] = "7",
            ["Jobs:Idempotency:IdempotencyIdsCanRepeat"] = "true",
            ["Jobs:Idempotency:EnableTraceLogging"] = "true"
        });

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<IdempotencyConfigurationModel>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal(45, options.ResultCacheDurationSeconds);
        Assert.Equal(7, options.MonitorIntervalSeconds);
        Assert.True(options.IdempotencyIdsCanRepeat);
        Assert.True(options.EnableTraceLogging);
    }

    [Theory]
    [InlineData(null, typeof(BatchModeJobLoader))]
    [InlineData("0", typeof(BatchModeJobLoader))]
    [InlineData("false", typeof(BatchModeJobLoader))]
    [InlineData("1", typeof(LoaderModeJobLoader))]
    [InlineData("true", typeof(LoaderModeJobLoader))]
    public void AddCoreJobManagement_ConfiguresJobLoaderFromUseLoaderMode(string? useLoaderMode,
        Type expectedLoaderType)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:UseLoaderMode"] = useLoaderMode
        });

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
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:BacklogSize"] = backlogSize.ToString()
        });

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
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["JobSource:FetchCount"] = batchSize.ToString()
        });

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JobSourceConfigurationModel>>().Value;
        Assert.Equal(batchSize, options.FetchCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(60)]
    public void AddCoreJobManagement_ConfiguresLoopOptions(int maxIdleWaitSeconds)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:MaxIdleWaitSeconds"] = maxIdleWaitSeconds.ToString()
        });

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
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:InternalRetryCount"] = internalRetryCount.ToString()
        });

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
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:WorkerThreadCount"] = workerThreadCount.ToString()
        });

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ThreadConfigurationModel>>().Value;
        Assert.Equal(workerThreadCount, options.WorkerThreadCount);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    public void AddCoreJobManagement_ConfiguresTimeBorderWrapper(int taskWaitBufferSeconds)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jobs:TaskWaitBufferSeconds"] = taskWaitBufferSeconds.ToString()
        });

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TimeBorderWrapperService.ConfigurationModel>>().Value;
        Assert.Equal(taskWaitBufferSeconds, options.TaskWaitBufferSeconds);
    }

    [Fact]
    public void AddCoreJobManagement_RegistersMessageSubscribeSourceStarter()
    {
        var configuration = CreateConfiguration();

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IJobSubscriberManager));
        Assert.Equal(typeof(JobSubscriberManager), descriptor.ImplementationType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void AddCoreJobManagement_WhenHealthDisabled_RegistersStubHealthServices(string? healthEnabled)
    {
        var values = new Dictionary<string, string?>();
        if (healthEnabled is not null)
        {
            values[$"{CommonHealthConfigurationModel.SectionName}:Enabled"] = healthEnabled;
        }

        var configuration = CreateConfiguration(values);

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(CoreHealthStateService));
        Assert.Equal(typeof(StubHealthStateService),
            Assert.Single(services, d => d.ServiceType == typeof(StubHealthStateService)).ImplementationType);
        Assert.NotNull(Assert.Single(services, d => d.ServiceType == typeof(ICoreStatisticsService))
            .ImplementationFactory);
        Assert.NotNull(Assert.Single(services, d => d.ServiceType == typeof(ICoreHealthStateReaderService))
            .ImplementationFactory);
        Assert.NotNull(Assert.Single(services, d => d.ServiceType == typeof(ICoreHealthStateUpdateService))
            .ImplementationFactory);

        using var provider = services.BuildServiceProvider();
        var stub = provider.GetRequiredService<StubHealthStateService>();
        Assert.Same(stub, provider.GetRequiredService<ICoreStatisticsService>());
        Assert.Same(stub, provider.GetRequiredService<ICoreHealthStateReaderService>());
        Assert.Same(stub, provider.GetRequiredService<ICoreHealthStateUpdateService>());
    }

    [Fact]
    public void AddCoreJobManagement_WhenHealthEnabled_RegistersCoreHealthServicesAndConfiguration()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            [$"{CommonHealthConfigurationModel.SectionName}:Enabled"] = "true",
            [$"{CommonHealthConfigurationModel.SectionName}:RecentIncidentThresholdSeconds"] = "90"
        });

        var services = new ServiceCollection()
            .AddCoreJobManagement(configuration);

        Assert.Equal(typeof(CoreStatisticsService),
            Assert.Single(services, d => d.ServiceType == typeof(ICoreStatisticsService)).ImplementationType);
        Assert.Equal(typeof(CoreHealthStateService),
            Assert.Single(services, d => d.ServiceType == typeof(CoreHealthStateService)).ImplementationType);
        Assert.NotNull(Assert.Single(services, d => d.ServiceType == typeof(ICoreHealthStateReaderService))
            .ImplementationFactory);
        Assert.NotNull(Assert.Single(services, d => d.ServiceType == typeof(ICoreHealthStateUpdateService))
            .ImplementationFactory);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(StubHealthStateService));

        using var provider = services.BuildServiceProvider();
        var healthOptions = provider.GetRequiredService<IOptions<CoreHealthStateService.ConfigurationModel>>().Value;
        Assert.Equal(90, healthOptions.RecentIncidentThresholdSeconds);

        var coreHealth = provider.GetRequiredService<CoreHealthStateService>();
        Assert.Same(coreHealth, provider.GetRequiredService<ICoreHealthStateReaderService>());
        Assert.Same(coreHealth, provider.GetRequiredService<ICoreHealthStateUpdateService>());
        Assert.IsType<CoreStatisticsService>(provider.GetRequiredService<ICoreStatisticsService>());
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