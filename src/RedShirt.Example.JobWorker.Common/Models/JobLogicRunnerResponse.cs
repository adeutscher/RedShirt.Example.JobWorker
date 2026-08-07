using RedShirt.Example.JobWorker.Common.Enums;
using RedShirt.Example.JobWorker.Common.Services.Abstractions;

namespace RedShirt.Example.JobWorker.Common.Models;

/// <summary>
///     Outcome of <see cref="IJobLogicRunner.RunAsync" />.
/// </summary>
public interface IJobLogicRunnerResponse
{
    /// <summary>
    ///     Logical job outcome previously returned directly as <see cref="JobResult" />.
    /// </summary>
    JobResult Result { get; }
}

/// <summary>
///     Basic <see cref="IJobLogicRunnerResponse" />.
/// </summary>
public sealed class JobLogicRunnerResponse : IJobLogicRunnerResponse
{
    public required JobResult Result { get; init; }
}