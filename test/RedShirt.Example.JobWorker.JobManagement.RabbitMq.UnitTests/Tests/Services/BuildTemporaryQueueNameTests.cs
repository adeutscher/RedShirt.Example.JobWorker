using RedShirt.Example.JobWorker.JobManagement.RabbitMq.Services;
using System.Reflection;

namespace RedShirt.Example.JobWorker.JobManagement.RabbitMq.UnitTests.Tests.Services;

public class BuildTemporaryQueueNameTests
{
    private static MethodInfo GetBuildTemporaryQueueNameMethod()
    {
        var method = typeof(RabbitMqConsumerQueueService).GetMethod(
            "BuildTemporaryQueueName",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        return method;
    }

    private static string InvokeBuildTemporaryQueueName(string exchangeName, string? label)
    {
        return (string) GetBuildTemporaryQueueNameMethod().Invoke(null, [exchangeName, label])!;
    }

    [Fact]
    public void BuildTemporaryQueueName_GeneratesUniqueNamesPerCall()
    {
        var first = InvokeBuildTemporaryQueueName("RabbitExchange", null);
        var second = InvokeBuildTemporaryQueueName("RabbitExchange", null);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BuildTemporaryQueueName_IsPrivateStaticMethodOnConsumerQueueService()
    {
        var method = GetBuildTemporaryQueueNameMethod();

        Assert.Equal("BuildTemporaryQueueName", method.Name);
        Assert.True(method.IsPrivate);
    }

    [Fact]
    public void BuildTemporaryQueueName_WhenExchangeNameMissing_Throws()
    {
        var method = GetBuildTemporaryQueueNameMethod();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, ["", null]));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void BuildTemporaryQueueName_WhenLabelIsWhitespace_OmitsLabelInfix()
    {
        var name = InvokeBuildTemporaryQueueName("RabbitExchange", "   ");

        Assert.Matches("^RabbitExchange-Temp-[0-9a-f]{32}$", name);
    }

    [Fact]
    public void BuildTemporaryQueueName_WhenLabelSet_IncludesLabelInfix()
    {
        var name = InvokeBuildTemporaryQueueName("RabbitExchange", "worker");

        Assert.Matches("^RabbitExchange-Temp-worker-[0-9a-f]{32}$", name);
    }

    [Fact]
    public void BuildTemporaryQueueName_WhenLabelUnset_UsesExchangeNameAndTempSegment()
    {
        var name = InvokeBuildTemporaryQueueName("RabbitExchange", null);

        Assert.Matches("^RabbitExchange-Temp-[0-9a-f]{32}$", name);
    }
}