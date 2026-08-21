using RedShirt.Example.JobWorker.Core.Enums;

namespace RedShirt.Example.JobWorker.Core.Services.Abstractions;

/// <summary>
///     Indicates a worker thread meant to be invoked and tracked by the Handler class.
/// </summary>
internal interface IHandlerSubComponent
{
    Task<HandlerComponentResponse> RunAsync(CancellationToken cancellationToken = default);
}