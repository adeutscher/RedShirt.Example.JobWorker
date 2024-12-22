namespace RedShirt.Example.JobWorker.IntegrationTests.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void Test_Get_Runner_Kinesis()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar",
            ["UseKinesis"] = "1"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }

    [Fact]
    public void Test_Get_Runner_SQS()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }
}