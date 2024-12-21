using Microsoft.Extensions.Logging;

namespace RedShirt.Example.JobWorker.Core.Services;

public interface IExecutionEndArbiter : IDisposable
{
    bool ShouldKeepRunning();
}

internal class ExecutionEndArbiter : IExecutionEndArbiter
{
    private readonly ILogger<ExecutionEndArbiter> _logger;

    public ExecutionEndArbiter(ILogger<ExecutionEndArbiter> logger)
    {
        _logger = logger;
        AppDomain.CurrentDomain.ProcessExit += HandleSigTerm;
    }

    internal bool IsRunning { get; set; } = true;

    public bool ShouldKeepRunning()
    {
        return IsRunning;
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= HandleSigTerm;
    }

    internal void HandleSigTerm(object? obj, EventArgs eventArgs)
    {
        _logger.LogInformation("Received SIGTERM");
        IsRunning = false;
    }
}