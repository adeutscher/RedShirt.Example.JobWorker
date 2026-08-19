using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Enums;
using RedShirt.Example.JobWorker.Core.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Services.ExecutionState;
using System.Runtime.ExceptionServices;

namespace RedShirt.Example.JobWorker.Core.Services.Jobs.Subscriptions;

internal interface IJobSubscriberExceptionRelay : IHandlerSubComponent;

internal class JobSubscriberExceptionRelay : IJobSubscriberExceptionRelay
{
    private readonly IOptions<CoreConfigurationModel> _coreOptions;
    private readonly IExecutionEndArbiter _executionEndArbiter;
    private readonly IJobSource _jobSource;
    private Exception? _exception;

    public JobSubscriberExceptionRelay(IJobSource jobSource, IExecutionEndArbiter executionEndArbiter,
        IOptions<CoreConfigurationModel> coreOptions)
    {
        _jobSource = jobSource;
        _executionEndArbiter = executionEndArbiter;
        _coreOptions = coreOptions;
        executionEndArbiter.AddOnStopCallback(e => _exception = e);
    }

    public async Task<HandlerComponentResponse> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_jobSource.IsSubscriptionSource || !_coreOptions.Value.HaltOnFailure)
        {
            // No need to watch
            return HandlerComponentResponse.NotEnabled;
        }

        await _executionEndArbiter.WaitForFinishedAsync(cancellationToken);

        if (_exception is not null)
        {
            // Going through ExceptionDispatchInfo in order to preserve stack trace outside a catch.
            ExceptionDispatchInfo.Capture(_exception).Throw();
        }

        return HandlerComponentResponse.Finished;
    }
}