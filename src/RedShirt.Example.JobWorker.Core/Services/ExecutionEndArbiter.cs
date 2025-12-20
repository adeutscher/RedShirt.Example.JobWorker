using Microsoft.Extensions.Logging;

namespace RedShirt.Example.JobWorker.Core.Services;

/// <summary>
///     Dictates if the app should continue running.
///     Written as a test-friendly alternative to `while(true){}`
/// </summary>
public interface IExecutionEndArbiter : IDisposable
{
    bool ShouldKeepRunning();
}

internal sealed class ExecutionEndArbiter : IExecutionEndArbiter
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