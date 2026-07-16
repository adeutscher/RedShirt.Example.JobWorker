using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Clients;
using RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.Factories;

namespace RedShirt.Example.JobWorker.JobManagement.AzureKeyVault.UnitTests.Tests.Factories;

public class AzureKeyVaultClientSourceTests
{
    [Fact]
    public void Test_Get()
    {
        var factory = new Mock<IAzureKeyVaultClientFactory>();
        factory.Setup(f => f.GetClient())
            .Returns(new Mock<IAzureKeyVaultClientWrapper>().Object);

        var source = new AzureKeyVaultClientSource(factory.Object);
        // Not called off the bat
        factory.Verify(f => f.GetClient(), Times.Never);

        var client = source.GetKeyVaultClient();
        Assert.NotNull(client);
        factory.Verify(f => f.GetClient(), Times.Once);

        var client2 = source.GetKeyVaultClient();
        Assert.NotNull(client2);
        Assert.Same(client, client2);

        // Still only once
        factory.Verify(f => f.GetClient(), Times.Once);
    }
}