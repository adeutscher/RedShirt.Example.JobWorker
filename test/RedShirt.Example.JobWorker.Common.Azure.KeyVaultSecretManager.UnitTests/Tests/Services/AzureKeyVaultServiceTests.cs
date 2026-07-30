using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Services;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Services;

public class AzureKeyVaultServiceTests
{
    public class GetSecretAsync
    {
        [Fact]
        public async Task ReturnsSecretValueFromClient()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            client.Setup(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken))
                .ReturnsAsync(value);

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var service = new AzureKeyVaultService(source.Object);

            var result = await service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            client.Verify(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
            source.VerifyNoOtherCalls();
            client.VerifyNoOtherCalls();
        }
    }

    public class GetSecretsAsync
    {
        [Fact]
        public async Task DeduplicatesKeysBeforeCallingClient()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            client.Setup(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken))
                .ReturnsAsync(value);

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var service = new AzureKeyVaultService(source.Object);

            var result = await service.GetSecretsAsync([key, key, key], TestContext.Current.CancellationToken);

            Assert.Equal(new Dictionary<string, string> {[key] = value}, result);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            client.Verify(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
            source.VerifyNoOtherCalls();
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EmptyList_DoesNotCallClientForSecrets()
        {
            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var service = new AzureKeyVaultService(source.Object);

            var result = await service.GetSecretsAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(result);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            source.VerifyNoOtherCalls();
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ReturnsSecretValuesForEachKey()
        {
            var keyA = Guid.NewGuid().ToString("N");
            var keyB = Guid.NewGuid().ToString("N");
            var valueA = Guid.NewGuid().ToString("N");
            var valueB = Guid.NewGuid().ToString("N");

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            client.Setup(c => c.GetSecretAsync(keyA, TestContext.Current.CancellationToken))
                .ReturnsAsync(valueA);
            client.Setup(c => c.GetSecretAsync(keyB, TestContext.Current.CancellationToken))
                .ReturnsAsync(valueB);

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var service = new AzureKeyVaultService(source.Object);

            var result = await service.GetSecretsAsync([keyA, keyB], TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
            Assert.Equal(valueA, result[keyA]);
            Assert.Equal(valueB, result[keyB]);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            client.Verify(c => c.GetSecretAsync(keyA, TestContext.Current.CancellationToken), Times.Once);
            client.Verify(c => c.GetSecretAsync(keyB, TestContext.Current.CancellationToken), Times.Once);
            source.VerifyNoOtherCalls();
            client.VerifyNoOtherCalls();
        }
    }
}