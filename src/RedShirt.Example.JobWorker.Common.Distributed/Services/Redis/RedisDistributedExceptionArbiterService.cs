using RedShirt.Example.JobWorker.Common.Azure.Models;

namespace RedShirt.Example.JobWorker.Common.Distributed.Services.Redis;

internal interface IRedisDistributedExceptionArbiterService
{
    RedisExceptionArbiterReport GetReport(Exception exception);
}

internal class RedisDistributedExceptionArbiterService : IRedisDistributedExceptionArbiterService 
{
    public RedisExceptionArbiterReport GetReport(Exception exception)
    {
        throw new NotImplementedException();
    }
}