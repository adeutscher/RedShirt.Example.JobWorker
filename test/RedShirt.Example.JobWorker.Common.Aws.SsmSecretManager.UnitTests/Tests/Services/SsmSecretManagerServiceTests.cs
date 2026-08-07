using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services;
using RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.Services.Resilience;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Aws.SsmSecretManager.UnitTests.Tests.Services;

public class SsmSecretManagerServiceTests
{
    private static string NewPath(string? prefix = null)
    {
        return string.IsNullOrEmpty(prefix)
            ? $"/{Guid.NewGuid():N}"
            : $"{prefix}/{Guid.NewGuid():N}";
    }

    private sealed class PassthroughRetryWrapper : ISsmRetryWrapperService
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> func, CancellationToken cancellationToken = default)
        {
            return func(cancellationToken);
        }
    }

    public class GetSecretAsync
    {
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("flat-name")]
        [InlineData("bad key")]
        [InlineData("/bad key")]
        [InlineData("/bad$key")]
        [InlineData("/trailing/")]
        [InlineData("/empty//segment")]
        [InlineData("/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p")]
        public async Task InvalidKey_ThrowsSecretManagerExceptionWithoutCallingSsm(string key)
        {
            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret path: {key}", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.False(thrown.CouldBeExternallySolvable);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task KeyLongerThan2048Characters_ThrowsSecretManagerException()
        {
            var key = "/" + new string('a', 2048);
            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret path: {key}", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PassesCancellationTokenToSsm()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var name = NewPath();
            var value = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParameterAsync(
                    It.Is<GetParameterRequest>(r => r.Name == name && r.WithDecryption == true),
                    cts.Token))
                .ReturnsAsync(new GetParameterResponse
                {
                    Parameter = new Parameter {Name = name, Value = value}
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretAsync(name, cts.Token);

            Assert.Equal(value, result);
            ssm.Verify(s => s.GetParameterAsync(
                It.Is<GetParameterRequest>(r => r.Name == name && r.WithDecryption == true),
                cts.Token), Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PropagatesSsmException()
        {
            var name = NewPath();

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParameterAsync(It.IsAny<GetParameterRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSimpleSystemsManagementException("parameter not found"));

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            await Assert.ThrowsAsync<AmazonSimpleSystemsManagementException>(() =>
                service.GetSecretAsync(name, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReturnsDecryptedParameterValue()
        {
            var name = NewPath();
            var value = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParameterAsync(
                    It.Is<GetParameterRequest>(r => r.Name == name && r.WithDecryption == true),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetParameterResponse
                {
                    Parameter = new Parameter
                    {
                        Name = name,
                        Value = value
                    }
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretAsync(name, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
            ssm.Verify(s => s.GetParameterAsync(
                It.Is<GetParameterRequest>(r => r.Name == name && r.WithDecryption == true),
                TestContext.Current.CancellationToken), Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("/a")]
        [InlineData("/common/redis")]
        [InlineData("/Dev/DBServer/MySQL/db-string13")]
        [InlineData("/path.with-dots_and-dashes/value")]
        [InlineData("/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o")]
        public async Task ValidKey_ReturnsDecryptedParameterValue(string key)
        {
            var value = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParameterAsync(
                    It.Is<GetParameterRequest>(r => r.Name == key && r.WithDecryption == true),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetParameterResponse
                {
                    Parameter = new Parameter {Name = key, Value = value}
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
            ssm.Verify(s => s.GetParameterAsync(
                It.Is<GetParameterRequest>(r => r.Name == key && r.WithDecryption == true),
                TestContext.Current.CancellationToken), Times.Once);
            ssm.VerifyNoOtherCalls();
        }
    }

    public class GetSecretsAsync
    {
        [Fact]
        public async Task DeduplicatesNamesBeforeCallingSsm()
        {
            var name = NewPath();
            var value = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(
                    It.Is<GetParametersRequest>(r =>
                        r.WithDecryption == true
                        && r.Names.Count == 1
                        && r.Names[0] == name),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetParametersResponse
                {
                    Parameters =
                    [
                        new Parameter {Name = name, Value = value}
                    ]
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync([name, name, name], TestContext.Current.CancellationToken);

            Assert.Equal(new Dictionary<string, string> {[name] = value}, result);
            ssm.Verify(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EmptyList_DoesNotCallSsm()
        {
            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(result);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExactlyTenNames_IsSingleRequest()
        {
            var names = Enumerable.Range(0, 10).Select(i => NewPath($"/{i}")).ToList();
            var values = names.ToDictionary(n => n, _ => Guid.NewGuid().ToString("N"));

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(
                    It.Is<GetParametersRequest>(r => r.WithDecryption == true && r.Names.Count == 10),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((GetParametersRequest request, CancellationToken _) => new GetParametersResponse
                {
                    Parameters = request.Names
                        .Select(name => new Parameter {Name = name, Value = values[name]})
                        .ToList()
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync(names, TestContext.Current.CancellationToken);

            Assert.Equal(values, result);
            ssm.Verify(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExactlyTwentyNames_IsTwoFullBatches()
        {
            var names = Enumerable.Range(0, 20).Select(i => NewPath($"/{i}")).ToList();
            var values = names.ToDictionary(n => n, _ => Guid.NewGuid().ToString("N"));
            var seenBatchSizes = new List<int>();

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GetParametersRequest request, CancellationToken _) =>
                {
                    seenBatchSizes.Add(request.Names.Count);
                    return new GetParametersResponse
                    {
                        Parameters = request.Names
                            .Select(name => new Parameter {Name = name, Value = values[name]})
                            .ToList()
                    };
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync(names, TestContext.Current.CancellationToken);

            Assert.Equal(values, result);
            Assert.Equal([10, 10], seenBatchSizes);
            ssm.Verify(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task InvalidKey_ReportsFirstInvalidKeyInListOrder()
        {
            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretsAsync(["/ok-key", "bad key", "/also-bad!"],
                    TestContext.Current.CancellationToken));

            Assert.Equal("Invalid secret path: bad key", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            ssm.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("flat-name")]
        [InlineData("bad key")]
        [InlineData("/bad$key")]
        public async Task InvalidKey_ThrowsBeforeCallingSsm(string badKey)
        {
            var validKey = NewPath();
            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                service.GetSecretsAsync([validKey, badKey], TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret path: {badKey}", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.False(thrown.CouldBeExternallySolvable);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task MoreThanTenNames_IsChunkedAcrossRequests()
        {
            var names = Enumerable.Range(0, 11).Select(i => NewPath($"/{i}")).ToList();
            var values = names.ToDictionary(n => n, _ => Guid.NewGuid().ToString("N"));

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GetParametersRequest request, CancellationToken _) => new GetParametersResponse
                {
                    Parameters = request.Names
                        .Select(name => new Parameter
                        {
                            Name = name,
                            Value = values[name]
                        })
                        .ToList()
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync(names, TestContext.Current.CancellationToken);

            Assert.Equal(values, result);
            ssm.Verify(s => s.GetParametersAsync(
                It.Is<GetParametersRequest>(r => r.WithDecryption == true && r.Names.Count == 10),
                It.IsAny<CancellationToken>()), Times.Once);
            ssm.Verify(s => s.GetParametersAsync(
                It.Is<GetParametersRequest>(r => r.WithDecryption == true && r.Names.Count == 1),
                It.IsAny<CancellationToken>()), Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task OmitsInvalidParametersFromResult()
        {
            var foundName = NewPath("/found");
            var missingName = NewPath("/missing");
            var foundValue = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(
                    It.IsAny<GetParametersRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetParametersResponse
                {
                    Parameters =
                    [
                        new Parameter {Name = foundName, Value = foundValue}
                    ],
                    InvalidParameters = [missingName]
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync([foundName, missingName], TestContext.Current.CancellationToken);

            Assert.Single(result);
            Assert.Equal(foundValue, result[foundName]);
            Assert.False(result.ContainsKey(missingName));
            ssm.Verify(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            ssm.Verify(
                s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), TestContext.Current.CancellationToken),
                Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PassesCancellationTokenToSsm()
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var name = NewPath();
            var value = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            // ReSharper disable once AccessToDisposedClosure
            ssm.Setup(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), cts.Token))
                .ReturnsAsync(new GetParametersResponse
                {
                    Parameters = [new Parameter {Name = name, Value = value}]
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync([name], cts.Token);

            Assert.Equal(value, result[name]);
            // ReSharper disable once AccessToDisposedClosure
            ssm.Verify(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), cts.Token), Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PropagatesSsmException()
        {
            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSimpleSystemsManagementException("ssm unavailable"));

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            await Assert.ThrowsAsync<AmazonSimpleSystemsManagementException>(() =>
                service.GetSecretsAsync(["/a"], TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReturnsDecryptedParameterValues()
        {
            var nameA = NewPath("/a");
            var nameB = NewPath("/b");
            var valueA = Guid.NewGuid().ToString("N");
            var valueB = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(
                    It.Is<GetParametersRequest>(r =>
                        r.WithDecryption == true
                        && r.Names.Count == 2
                        && r.Names.Contains(nameA)
                        && r.Names.Contains(nameB)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetParametersResponse
                {
                    Parameters =
                    [
                        new Parameter {Name = nameA, Value = valueA},
                        new Parameter {Name = nameB, Value = valueB}
                    ]
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync([nameA, nameB], TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
            Assert.Equal(valueA, result[nameA]);
            Assert.Equal(valueB, result[nameB]);
            ssm.Verify(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            ssm.Verify(
                s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), TestContext.Current.CancellationToken),
                Times.Once);
            ssm.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("/a")]
        [InlineData("/common/redis")]
        [InlineData("/Dev/DBServer/MySQL/db-string13")]
        public async Task ValidKeys_ReturnDecryptedParameterValues(string key)
        {
            var value = Guid.NewGuid().ToString("N");

            var ssm = new Mock<IAmazonSimpleSystemsManagement>(MockBehavior.Strict);
            ssm.Setup(s => s.GetParametersAsync(
                    It.Is<GetParametersRequest>(r =>
                        r.WithDecryption == true && r.Names.Count == 1 && r.Names[0] == key),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetParametersResponse
                {
                    Parameters = [new Parameter {Name = key, Value = value}]
                });

            var service = new SsmSecretManagerService(ssm.Object, new PassthroughRetryWrapper());

            var result = await service.GetSecretsAsync([key], TestContext.Current.CancellationToken);

            Assert.Equal(new Dictionary<string, string> {[key] = value}, result);
            ssm.Verify(s => s.GetParametersAsync(It.IsAny<GetParametersRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            ssm.VerifyNoOtherCalls();
        }
    }
}