namespace RedShirt.Example.JobWorker.IntegrationTests.Tests;

[Collection("DependencyInjectionTests")]
public class DependencyInjectionTests
{
    [Fact]
    public void Test_Get_Runner_Active()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseActiveMq"] = "1",
            ["UseAzureQueueStorage"] = "0",
            ["UseNats"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "0"
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
            ["UseNats"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "0"
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
            ["UseNats"] = "0",
            ["UseRabbitMq"] = "0",
            ["UseKinesis"] = "1"
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
            ["UseNats"] = "1",
            ["UseRabbitMq"] = "0"
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
            ["UseKinesis"] = "0",
            ["UseNats"] = "0",
            ["UseRabbitMq"] = "1"
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
            ["UseActiveMq"] = "0",
            ["UseAzureQueueStorage"] = "0",
            ["UseKinesis"] = "0",
            ["UseNats"] = "0",
            ["UseRabbitMq"] = "0"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }
}