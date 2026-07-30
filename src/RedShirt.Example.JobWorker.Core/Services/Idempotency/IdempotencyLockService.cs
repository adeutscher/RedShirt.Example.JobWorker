using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Distributed.Models;
using RedShirt.Example.JobWorker.Common.Distributed.Services.Abstractions;
using RedShirt.Example.JobWorker.Core.Configuration;
using RedShirt.Example.JobWorker.Core.Models;

namespace RedShirt.Example.JobWorker.Core.Services.Idempotency;

internal class IdempotencyLockService(
    IAbstractedLockService abstractedLockService,
    IOptions<IdempotencyConfigurationModel> options)
{
    public async Task<IAbstractedLock> GetLockAsync(IJobModel jobModel, CancellationToken token)
    {
        if (!options.Value.Enabled || string.IsNullOrWhiteSpace(jobModel.IdempotencyId))
        {
            /*
             * The method invoking this method doesn't need to be aware of the ins and outs of idempotency.
             * All it needs to know is that it's clear to proceed with execution.
             */
            return new EmptyIdempotencyLock();
        }

        return await abstractedLockService.GetLockAsync($"lock:idempotency:{jobModel.IdempotencyId}", token);
    }

    /// <summary>
    ///     Fakes a successful lock acquisition in order to simplify handling elsewhere.
    /// </summary>
    private sealed class EmptyIdempotencyLock : IAbstractedLock
    {
        public bool IsAcquired => true;

        public void Unlock()
        {
        }
    }
}