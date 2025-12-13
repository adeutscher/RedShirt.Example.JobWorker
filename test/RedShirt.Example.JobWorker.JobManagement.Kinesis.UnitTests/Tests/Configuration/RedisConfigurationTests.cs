using RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;

namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.UnitTests.Tests.Configuration;

public class RedisConfigurationTests
{
    [Fact]
    public void Test_ConfigModel()
    {
        var a = Guid.NewGuid().ToString();

        var redisConfig = new RedisConfiguration
        {
            EndpointAddress = a,
            EndpointPort = 1234
        };
        Assert.Equal(a, redisConfig.EndpointAddress);
        Assert.Equal(1234, redisConfig.EndpointPort);
    }
}