using Microsoft.Extensions.Logging;
using RedShirt.Example.JobWorker.Core.Utility;

namespace RedShirt.Example.JobWorker.Core.Services.ExecutionState;

/// <summary>
///     Dictates if the app should continue running.
///     Originally written as a test-friendly alternative to `while(true){}`
/// </summary>
public interface IExecutionEndArbiter : IDisposable
{
    /// <summary>
    ///     Cancelled when <see cref="Stop" /> is invoked.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    ///     Thread-safe addition of callback actions.
    /// </summary>
    /// <param name="callback"></param>
    void AddOnStopCallback(Action<Exception?> callback);

    bool ShouldKeepRunning();

    /// <summary>
    ///     Mark the stop of operations. Only runs once in a thread-safe manner.
    /// </summary>
    /// <param name="exception"></param>
    void Stop(Exception? exception = null);

    Task WaitForFinishedAsync(CancellationToken cancellationToken = default);
}

internal sealed class ExecutionEndArbiter : IExecutionEndArbiter
{
    private readonly Lock _lock = new();
    private readonly ILogger<ExecutionEndArbiter> _logger;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly AsyncManualResetEvent _stoppedEvent = new();
    private Exception? _exception;
    private Action<Exception?>? _primaryCallbacks;

    public ExecutionEndArbiter(ILogger<ExecutionEndArbiter> logger)
    {
        _logger = logger;

        AppDomain.CurrentDomain.ProcessExit += HandleSigTerm;
    }

    internal bool IsRunning { get; private set; } = true;

    public CancellationToken CancellationToken => _stopCts.Token;

    public bool ShouldKeepRunning()
    {
        return IsRunning;
    }

    void IDisposable.Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= HandleSigTerm;
        _stopCts.Dispose();
    }

    public void AddOnStopCallback(Action<Exception?> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_lock)
        {
            if (IsRunning)
            {
                _primaryCallbacks += callback;

                return;
            }
        }

        callback(_exception);
    }

    public void Stop(Exception? exception = null)
    {
        Action<Exception?>? primaryCallbacks;
        lock (_lock)
        {
            if (!IsRunning)
            {
                return;
            }

            _logger.LogTrace("Stopping application");

            IsRunning = false;
            _exception = exception;
            primaryCallbacks = _primaryCallbacks;
            _primaryCallbacks = null;
        }

        primaryCallbacks?.Invoke(exception);

        // Call set after all callbacks have been run to signal anything that might be waiting.
        _stoppedEvent.Set();

        try
        {
            _stopCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Dispose may have already run (e.g. tests); stop signaling is still complete.
        }
    }

    public Task WaitForFinishedAsync(CancellationToken cancellationToken = default)
    {
        return _stoppedEvent.WaitAsync(cancellationToken);
    }

    internal void HandleSigTerm(object? obj, EventArgs eventArgs)
    {
        _logger.LogInformation("Received SIGTERM");
        Stop();
    }
}