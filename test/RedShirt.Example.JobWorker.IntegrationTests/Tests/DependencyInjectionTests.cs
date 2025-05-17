namespace RedShirt.Example.JobWorker.IntegrationTests.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void Test_Get_Runner_Kinesis()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["AWS_ACCESS_KEY_ID"] = "foo",
            ["AWS_SECRET_ACCESS_KEY"] = "bar",
            ["AWS_SESSION_TOKEN"] = "foobar",
            ["UseKinesis"] = "1"
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
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }
}