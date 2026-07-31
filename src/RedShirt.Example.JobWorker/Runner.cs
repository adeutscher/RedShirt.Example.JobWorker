using RedShirt.Example.JobWorker.Core.Services;

namespace RedShirt.Example.JobWorker;

public sealed class Runner(IHandler handler)
{
    public Task RunAsync()
    {
        // Potentially put CLI-arg handling here. Otherwise, just a pass-through to handler in Core.
        return handler.HandleAsync();
    }
}