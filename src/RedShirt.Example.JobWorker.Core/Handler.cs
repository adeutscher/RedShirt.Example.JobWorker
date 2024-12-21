namespace RedShirt.Example.JobWorker.Core;

public interface IHandler
{
    Task HandleAsync();
}

internal class Handler : IHandler
{
    public async Task HandleAsync()
    {
    }
}