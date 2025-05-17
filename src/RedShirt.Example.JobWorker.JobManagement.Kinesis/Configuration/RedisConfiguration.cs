namespace RedShirt.Example.JobWorker.JobManagement.Kinesis.Configuration;

internal class RedisConfiguration
{
    public required string EndpointAddress { get; set; }
    public required int EndpointPort { get; set; }
}