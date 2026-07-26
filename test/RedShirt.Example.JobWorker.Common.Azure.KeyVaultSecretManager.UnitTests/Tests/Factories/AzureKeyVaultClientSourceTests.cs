using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Factories;

public class AzureKeyVaultClientSourceTests
{
    [Fact]
    public void GetKeyVaultClient_LazilyCreatesAndCachesClient()
    {
        var factory = new Mock<IAzureKeyVaultClientFactory>(MockBehavior.Strict);
        factory.Setup(f => f.GetClient())
            .Returns(new Mock<IAzureKeyVaultClientWrapper>().Object);

        var source = new AzureKeyVaultClientSource(factory.Object);

        factory.Verify(f => f.GetClient(), Times.Never);

        var client = source.GetKeyVaultClient();
        Assert.NotNull(client);
        factory.Verify(f => f.GetClient(), Times.Once);

        var client2 = source.GetKeyVaultClient();
        Assert.NotNull(client2);
        Assert.Same(client, client2);
        factory.Verify(f => f.GetClient(), Times.Once);
        factory.VerifyNoOtherCalls();
    }
}
