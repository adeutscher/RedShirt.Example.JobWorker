using RedShirt.Example.JobWorker.Common.Azure.Exceptions;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Clients;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Factories;
using RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.Services;
using RedShirt.Example.JobWorker.Common.Azure.Services;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Azure.KeyVaultSecretManager.UnitTests.Tests.Services;

public class AzureKeyVaultServiceTests
{
    private static Mock<IAzureRetryWrapperService> CreatePassthroughRetryWrapper()
    {
        var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string>>, CancellationToken>((func, ct) => func(ct));
        retry
            .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>, CancellationToken>((func, ct) =>
                func(ct));
        return retry;
    }

    public class GetSecretAsync
    {
        [Theory]
        [InlineData("")]
        [InlineData("bad key")]
        [InlineData("bad/key")]
        [InlineData("bad.key")]
        [InlineData("bad_key")]
        public async Task InvalidKey_ThrowsSecretManagerExceptionWithoutCallingDependencies(string key)
        {
            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);

            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret path: {key}", thrown.Message);
            Assert.False(thrown.IsCritical);
            Assert.False(thrown.IsTransient);
            source.VerifyNoOtherCalls();
            client.VerifyNoOtherCalls();
            retry.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task KeyLongerThan127Characters_ThrowsSecretManagerException()
        {
            var key = new string('a', 128);
            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret path: {key}", thrown.Message);
            Assert.False(thrown.IsCritical);
            Assert.False(thrown.IsTransient);
            source.VerifyNoOtherCalls();
            retry.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PassesCancellationTokenToRetryWrapper()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var key = Guid.NewGuid().ToString("N");
            CancellationToken? seenToken = null;

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            // ReSharper disable once AccessToDisposedClosure
            client.Setup(c => c.GetSecretAsync(key, cts.Token)).ReturnsAsync("value");

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            retry
                .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<string>>, CancellationToken>((func, ct) =>
                {
                    seenToken = ct;
                    return func(ct);
                });

            var service = new AzureKeyVaultService(retry.Object, source.Object);

            await service.GetSecretAsync(key, cts.Token);

            Assert.Equal(cts.Token, seenToken);
        }

        [Fact]
        public async Task ReturnsSecretValueThroughRetryWrapper()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            client.Setup(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken))
                .ReturnsAsync(value);

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var retry = CreatePassthroughRetryWrapper();
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var result = await service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            client.Verify(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
            retry.Verify(
                r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    TestContext.Current.CancellationToken), Times.Once);
            source.VerifyNoOtherCalls();
            client.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("a")]
        [InlineData("secret-name")]
        [InlineData("Secret-Name-123")]
        public async Task ValidKey_ReturnsSecretValue(string key)
        {
            var value = Guid.NewGuid().ToString("N");

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            client.Setup(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken))
                .ReturnsAsync(value);

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var retry = CreatePassthroughRetryWrapper();
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var result = await service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
            client.Verify(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
        }

        [Fact]
        public async Task WhenRetryWrapperThrowsWorkerAzureException_Propagates()
        {
            var key = Guid.NewGuid().ToString("N");
            var inner = new WorkerAzureException("vault unavailable", false, true);

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            retry
                .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    TestContext.Current.CancellationToken))
                .ThrowsAsync(inner);

            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var thrown = await Assert.ThrowsAsync<WorkerAzureException>(() =>
                service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Same(inner, thrown);
            source.VerifyNoOtherCalls();
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

            var retry = CreatePassthroughRetryWrapper();
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var result = await service.GetSecretsAsync([key, key, key], TestContext.Current.CancellationToken);

            Assert.Equal(new Dictionary<string, string> {[key] = value}, result);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            client.Verify(c => c.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
            retry.Verify(
                r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    TestContext.Current.CancellationToken), Times.Once);
        }

        [Fact]
        public async Task EmptyList_ResolvesClientOnceAndDoesNotFetchSecrets()
        {
            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var retry = CreatePassthroughRetryWrapper();
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var result = await service.GetSecretsAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(result);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            client.VerifyNoOtherCalls();
            retry.Verify(
                r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>>(),
                    TestContext.Current.CancellationToken), Times.Once);
            retry.Verify(
                r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task InvalidKey_ReportsFirstInvalidKeyInListOrder()
        {
            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretsAsync(["ok-key", "bad key", "also bad!"], TestContext.Current.CancellationToken));

            Assert.Equal("Invalid secret path: bad key", thrown.Message);
        }

        [Theory]
        [InlineData("bad key")]
        [InlineData("bad/key")]
        [InlineData("")]
        public async Task InvalidKey_ThrowsBeforeResolvingClient(string badKey)
        {
            var validKey = Guid.NewGuid().ToString("N");
            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretsAsync([validKey, badKey], TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret path: {badKey}", thrown.Message);
            source.VerifyNoOtherCalls();
            retry.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PassesCancellationTokenToRetryWrapper()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var key = Guid.NewGuid().ToString("N");
            var seenTokens = new List<CancellationToken>();

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            // ReSharper disable once AccessToDisposedClosure
            client.Setup(c => c.GetSecretAsync(key, cts.Token)).ReturnsAsync("value");

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            retry
                .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>, CancellationToken>((func, ct) =>
                {
                    seenTokens.Add(ct);
                    return func(ct);
                });
            retry
                .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<Func<CancellationToken, Task<string>>, CancellationToken>((func, ct) =>
                {
                    seenTokens.Add(ct);
                    return func(ct);
                });

            var service = new AzureKeyVaultService(retry.Object, source.Object);

            await service.GetSecretsAsync([key], cts.Token);

            Assert.Equal(2, seenTokens.Count);
            Assert.All(seenTokens, token => Assert.Equal(cts.Token, token));
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

            var retry = CreatePassthroughRetryWrapper();
            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var result = await service.GetSecretsAsync([keyA, keyB], TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
            Assert.Equal(valueA, result[keyA]);
            Assert.Equal(valueB, result[keyB]);
            source.Verify(s => s.GetKeyVaultClient(), Times.Once);
            client.Verify(c => c.GetSecretAsync(keyA, TestContext.Current.CancellationToken), Times.Once);
            client.Verify(c => c.GetSecretAsync(keyB, TestContext.Current.CancellationToken), Times.Once);
            retry.Verify(
                r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>>(),
                    TestContext.Current.CancellationToken), Times.Once);
            retry.Verify(
                r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    TestContext.Current.CancellationToken), Times.Exactly(2));
        }

        [Fact]
        public async Task WhenSecretFetchThrowsWorkerAzureException_WrapsAsSecretManagerException()
        {
            var key = Guid.NewGuid().ToString("N");
            var azureException = new WorkerAzureException("get failed", false);

            var client = new Mock<IAzureKeyVaultClientWrapper>(MockBehavior.Strict);
            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            source.Setup(s => s.GetKeyVaultClient()).Returns(client.Object);

            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            retry
                .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>>(),
                    TestContext.Current.CancellationToken))
                .ReturnsAsync(client.Object);
            retry
                .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<string>>>(),
                    TestContext.Current.CancellationToken))
                .ThrowsAsync(azureException);

            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretsAsync([key], TestContext.Current.CancellationToken));

            Assert.Same(azureException, thrown.InnerException);
            Assert.False(thrown.IsCritical);
            Assert.False(thrown.IsTransient);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task WhenWorkerAzureException_WrapsAsSecretManagerException(bool isTransient)
        {
            var key = Guid.NewGuid().ToString("N");
            var azureException = new WorkerAzureException("vault unavailable", false, isTransient);

            var source = new Mock<IAzureKeyVaultClientSource>(MockBehavior.Strict);
            var retry = new Mock<IAzureRetryWrapperService>(MockBehavior.Strict);
            retry
                .Setup(r => r.RunAsync(It.IsAny<Func<CancellationToken, Task<IAzureKeyVaultClientWrapper>>>(),
                    TestContext.Current.CancellationToken))
                .ThrowsAsync(azureException);

            var service = new AzureKeyVaultService(retry.Object, source.Object);

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretsAsync([key], TestContext.Current.CancellationToken));

            Assert.Same(azureException, thrown.InnerException);
            Assert.False(thrown.IsCritical);
            Assert.Equal(isTransient, thrown.IsTransient);
            source.VerifyNoOtherCalls();
        }
    }
}