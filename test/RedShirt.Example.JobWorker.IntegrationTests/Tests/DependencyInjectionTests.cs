namespace RedShirt.Example.JobWorker.IntegrationTests.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void Test_Get_Runner()
    {
        TestUtilities.WrapEnvironment(new Dictionary<string, string>
        {
            ["AWS_SERVICE_URL"] = "http://foo.bar"
        }, () => { Assert.NotNull(Setup.GetRunner()); });
    }
}