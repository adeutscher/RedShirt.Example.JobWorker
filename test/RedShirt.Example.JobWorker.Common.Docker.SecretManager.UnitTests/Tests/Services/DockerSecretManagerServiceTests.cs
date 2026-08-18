using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.Docker.SecretManager.Services;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Exceptions;

namespace RedShirt.Example.JobWorker.Common.Docker.SecretManager.UnitTests.Tests.Services;

public class DockerSecretManagerServiceTests
{
    private sealed class SecretDirectory : IDisposable
    {
        public string Path { get; }

        public DockerSecretManagerService Service { get; }

        public SecretDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Service = new DockerSecretManagerService(Options.Create(new DockerSecretManagerService.ConfigurationModel
            {
                Directory = Path
            }));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        public void Write(string fileName, string contents)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), contents);
        }
    }

    public class ConfigurationModel
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        public void EffectiveDirectory_WhenDirectoryIsMissing_DefaultsToRunSecrets(string? directory)
        {
            var model = new DockerSecretManagerService.ConfigurationModel {Directory = directory};

            Assert.Equal("/run/secrets", model.EffectiveDirectory);
        }

        [Fact]
        public void EffectiveDirectory_WhenDirectoryIsSet_UsesConfiguredValue()
        {
            var directory = $"/tmp/{Guid.NewGuid():N}";
            var model = new DockerSecretManagerService.ConfigurationModel {Directory = directory};

            Assert.Equal(directory, model.EffectiveDirectory);
        }
    }

    public class GetSecretAsync
    {
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        public async Task BlankKey_ThrowsSecretManagerException(string key)
        {
            using var secrets = new SecretDirectory();

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal("Secret key is required", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.False(thrown.CouldBeExternallySolvable);
        }

        [Theory]
        [InlineData("bad key")]
        [InlineData("bad/key")]
        [InlineData("bad.key")]
        [InlineData("bad@key")]
        public async Task InvalidKey_ThrowsSecretManagerException(string key)
        {
            using var secrets = new SecretDirectory();

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret key: {key}", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.False(thrown.CouldBeExternallySolvable);
        }

        [Fact]
        public async Task KeyLongerThan250Characters_ThrowsSecretManagerException()
        {
            var key = new string('a', 251);
            using var secrets = new SecretDirectory();

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal($"Invalid secret key: {key}", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.False(thrown.CouldBeExternallySolvable);
        }

        [Fact]
        public async Task MissingFile_ThrowsSecretManagerException()
        {
            var key = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken));

            Assert.Equal($"Secret file not found: {key}", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.True(thrown.CouldBeExternallySolvable);
        }

        [Fact]
        public async Task PassesCancellationTokenToFileRead()
        {
            var key = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(key, "value");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                secrets.Service.GetSecretAsync(key, cts.Token));
        }

        [Fact]
        public async Task PrefersExtensionlessFileOverTxtAndJsonFallbacks()
        {
            var key = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(key, "plain");
            secrets.Write($"{key}.txt", "txt");
            secrets.Write($"{key}.json", "json");

            var result = await secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal("plain", result);
        }

        [Fact]
        public async Task PrefersTxtFallbackOverJson()
        {
            var key = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write($"{key}.txt", "txt");
            secrets.Write($"{key}.json", "json");

            var result = await secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal("txt", result);
        }

        [Fact]
        public async Task ReturnsFileContents()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(key, value);

            var result = await secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
        }

        [Fact]
        public async Task ReturnsJsonFallbackWhenOnlyJsonExists()
        {
            var key = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write($"{key}.json", "{\"secret\":\"value\"}");

            var result = await secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal("{\"secret\":\"value\"}", result);
        }

        [Fact]
        public async Task TrimsOnlyTrailingNewlines()
        {
            var key = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(key, "value \r\n\n");

            var result = await secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal("value ", result);
        }

        [Fact]
        public async Task ValidKeyOfMaxLength_ReturnsSecretValue()
        {
            var key = new string('a', 250);
            var value = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(key, value);

            var result = await secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("secret-name")]
        [InlineData("Secret_Name-123")]
        public async Task ValidKey_ReturnsSecretValue(string key)
        {
            var value = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(key, value);

            var result = await secrets.Service.GetSecretAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(value, result);
        }
    }

    public class GetSecretsAsync
    {
        [Fact]
        public async Task DeduplicatesKeysBeforeReadingFiles()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(key, value);

            var result = await secrets.Service.GetSecretsAsync([key, key, key],
                TestContext.Current.CancellationToken);

            Assert.Equal(new Dictionary<string, string> {[key] = value}, result);
        }

        [Fact]
        public async Task DistinctKeysAreCaseSensitive()
        {
            using var secrets = new SecretDirectory();
            secrets.Write("Secret", "upper");
            secrets.Write("secret", "lower");

            var result = await secrets.Service.GetSecretsAsync(["Secret", "secret"],
                TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
            Assert.Equal("upper", result["Secret"]);
            Assert.Equal("lower", result["secret"]);
        }

        [Fact]
        public async Task EmptyList_ReturnsEmptyDictionary()
        {
            using var secrets = new SecretDirectory();

            var result = await secrets.Service.GetSecretsAsync([], TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }

        [Fact]
        public async Task InvalidKey_ThrowsSecretManagerException()
        {
            var validKey = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(validKey, "value");

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                secrets.Service.GetSecretsAsync([validKey, "bad key"], TestContext.Current.CancellationToken));

            Assert.Equal("Invalid secret key: bad key", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.False(thrown.CouldBeExternallySolvable);
        }

        [Fact]
        public async Task MissingFile_ThrowsSecretManagerException()
        {
            var foundKey = Guid.NewGuid().ToString("N");
            var missingKey = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(foundKey, "value");

            var thrown = await Assert.ThrowsAsync<WorkerSecretManagerException>(() =>
                secrets.Service.GetSecretsAsync([foundKey, missingKey], TestContext.Current.CancellationToken));

            Assert.Equal($"Secret file not found: {missingKey}", thrown.Message);
            Assert.False(thrown.CouldBeTransient);
            Assert.False(thrown.IsHandled);
            Assert.True(thrown.CouldBeExternallySolvable);
        }

        [Fact]
        public async Task NullKeys_ThrowsArgumentNullException()
        {
            using var secrets = new SecretDirectory();

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                secrets.Service.GetSecretsAsync(null!, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ReturnsSecretValuesForEachKey()
        {
            var keyA = Guid.NewGuid().ToString("N");
            var keyB = Guid.NewGuid().ToString("N");
            var valueA = Guid.NewGuid().ToString("N");
            var valueB = Guid.NewGuid().ToString("N");
            using var secrets = new SecretDirectory();
            secrets.Write(keyA, valueA);
            secrets.Write($"{keyB}.txt", $"{valueB}\n");

            var result = await secrets.Service.GetSecretsAsync([keyA, keyB], TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
            Assert.Equal(valueA, result[keyA]);
            Assert.Equal(valueB, result[keyB]);
        }
    }

    public class IsUnderDirectory
    {
        [Fact]
        public void DirectoryItself_ReturnsTrue()
        {
            using var secrets = new SecretDirectory();

            Assert.True(DockerSecretManagerService.IsUnderDirectory(secrets.Path, secrets.Path));
        }

        [Fact]
        public void PathOutsideDirectory_ReturnsFalse()
        {
            using var secrets = new SecretDirectory();
            var other = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            Assert.False(DockerSecretManagerService.IsUnderDirectory(other, secrets.Path));
        }

        [Fact]
        public void PathSharingPrefixWithoutSeparator_ReturnsFalse()
        {
            using var secrets = new SecretDirectory();
            var similar = secrets.Path + "-extra";

            Assert.False(DockerSecretManagerService.IsUnderDirectory(similar, secrets.Path));
        }

        [Fact]
        public void PathUnderDirectory_ReturnsTrue()
        {
            using var secrets = new SecretDirectory();
            var child = Path.Combine(secrets.Path, "nested", "secret");

            Assert.True(DockerSecretManagerService.IsUnderDirectory(child, secrets.Path));
        }

        [Fact]
        public void RelativeEscape_ReturnsFalse()
        {
            using var secrets = new SecretDirectory();
            var escaped = Path.Combine(secrets.Path, "..", Guid.NewGuid().ToString("N"));

            Assert.False(DockerSecretManagerService.IsUnderDirectory(escaped, secrets.Path));
        }

        [Fact]
        public void TrailingDirectorySeparator_IsIgnoredOnRoot()
        {
            using var secrets = new SecretDirectory();
            var child = Path.Combine(secrets.Path, "secret");

            Assert.True(DockerSecretManagerService.IsUnderDirectory(child,
                secrets.Path + Path.DirectorySeparatorChar));
        }
    }
}