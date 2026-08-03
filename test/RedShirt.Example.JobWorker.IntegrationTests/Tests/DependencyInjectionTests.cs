namespace RedShirt.Example.JobWorker.IntegrationTests.Tests;

[Collection("DependencyInjectionTests")]
public class DependencyInjectionTests
{
    [Fact]
    public void Test_Get_Runner_ActiveMq()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "1",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_AzureQueueStorage()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "1",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_AzureServiceBus()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "1",
            ["UseGooglePubSub"] = "0",
            ["UseNats"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_GooglePubSub()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "1",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_Kafka()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "0",
            ["UseKafka"] = "1"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_Kinesis()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "1",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_Nats()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseKinesis"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseNats"] = "1",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_RabbitMq()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseKinesis"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "1",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_RedisStreams()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseKinesis"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "1",
            ["UseRabbitMq"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_SQS()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["JOBS__LOADER__ENABLED"] = "0",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseKinesis"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_SQS_And_Loader_Mode()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["JOBS__LOADER__ENABLED"] = "1",
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseAzureServiceBus"] = "0",
            ["UseGooglePubSub"] = "0",
            ["UseKinesis"] = "0",
            ["UseNats"] = "0",
            ["UseRedisStreams"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKafka"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }
}