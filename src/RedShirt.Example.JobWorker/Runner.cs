using RedShirt.Example.JobWorker.Core;

namespace RedShirt.Example.JobWorker;

public class Runner(IHandler handler)
{
    public Task RunAsync()
    {
        // Potentially put CLI-arg handling here. Otherwise, just a pass-through to handler in Core.
        return handler.HandleAsync();
    }
}