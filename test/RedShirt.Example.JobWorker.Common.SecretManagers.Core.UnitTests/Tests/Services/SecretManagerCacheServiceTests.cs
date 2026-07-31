using Microsoft.Extensions.Options;
using RedShirt.Example.JobWorker.Common.SecretManagers.Core.Services;

namespace RedShirt.Example.JobWorker.Common.SecretManagers.Core.UnitTests.Tests.Services;

public class SecretManagerCacheServiceTests
{
    public class GetSecretAsync
    {
        [Fact]
        public async Task ExpiredEntry_IsRefetchedWithoutForce()
        {
            var key = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets.SetupSequence(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken))
                .ReturnsAsync("stale")
                .ReturnsAsync("fresh");

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            Assert.Equal("stale",
                await cache.GetSecretAsync(key, TimeSpan.FromMilliseconds(40),
                    cancellationToken: TestContext.Current.CancellationToken));

            await Task.Delay(TimeSpan.FromMilliseconds(60), TestContext.Current.CancellationToken);

            Assert.Equal("fresh",
                await cache.GetSecretAsync(key, cancellationToken: TestContext.Current.CancellationToken));

            secrets.Verify(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Exactly(2));
        }

        [Fact]
        public async Task Force_AfterCooldown_CallsUnderlyingServiceAgain()
        {
            var key = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets.SetupSequence(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken))
                .ReturnsAsync("v1")
                .ReturnsAsync("v2");

            // EffectiveForceCooldownSeconds floors at 1
            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 1
                }));

            Assert.Equal("v1",
                await cache.GetSecretAsync(key, cancellationToken: TestContext.Current.CancellationToken));

            await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);

            Assert.Equal("v2",
                await cache.GetSecretAsync(key, force: true,
                    cancellationToken: TestContext.Current.CancellationToken));

            secrets.Verify(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Exactly(2));
        }

        [Fact]
        public async Task Force_WhileCooldownActive_DoesNotCallUnderlyingService()
        {
            var key = Guid.NewGuid().ToString("N");
            var secret = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken)).ReturnsAsync(secret);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 120
                }));

            // Call the first time
            var first = await cache.GetSecretAsync(key, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(secret, first);
            var again = await cache.GetSecretAsync(key, force: true,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(secret, again);
            secrets.Verify(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Miss_CallsUnderlyingServiceOnceAndCachesResult()
        {
            var key = Guid.NewGuid().ToString("N");
            var secret = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken)).ReturnsAsync(secret);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            var first = await cache.GetSecretAsync(key, cancellationToken: TestContext.Current.CancellationToken);
            var second = await cache.GetSecretAsync(key, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(secret, first);
            Assert.Same(first, second);
            secrets.Verify(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task NullExpiration_KeepsEntryUntilForcedOrOtherwiseInvalidated()
        {
            var key = Guid.NewGuid().ToString("N");
            var secret = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken)).ReturnsAsync(secret);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            var first = await cache.GetSecretAsync(key,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(secret, first);
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
            var cached = await cache.GetSecretAsync(key, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(secret, cached);
            secrets.Verify(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
        }

        [Fact]
        public async Task PreCancelledToken_ThrowsBeforeServiceCall()
        {
            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                cache.GetSecretAsync(Guid.NewGuid().ToString("N"),
                    cancellationToken: new CancellationToken(true)));

            secrets.VerifyNoOtherCalls();
        }
    }

    public class GetSecretsAsync
    {
        [Fact]
        public async Task AllKeysCached_SkipsServiceEntirely()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken)).ReturnsAsync(value);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));
            _ = await cache.GetSecretAsync(key, cancellationToken: TestContext.Current.CancellationToken);

            var result = await cache.GetSecretsAsync([key], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(value, result[key]);
            secrets.Verify(s => s.GetSecretAsync(key, TestContext.Current.CancellationToken), Times.Once);
            secrets.Verify(
                s => s.GetSecretsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task DuplicateKeys_AreCollapsedBeforeFetch()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets
                .Setup(s => s.GetSecretsAsync(It.Is<List<string>>(keys =>
                        keys.Count() == 1 && keys.Single() == key),
                    TestContext.Current.CancellationToken))
                .ReturnsAsync(new Dictionary<string, string> {[key] = value});

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            var result = await cache.GetSecretsAsync([key, key, key],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(new Dictionary<string, string> {[key] = value}, result);
            secrets.Verify(
                s => s.GetSecretsAsync(It.IsAny<List<string>>(), TestContext.Current.CancellationToken),
                Times.Once);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EmptyRequest_ReturnsEmptyAndSkipsService()
        {
            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            var result = await cache.GetSecretsAsync([], cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Force_AfterCooldown_RefetchesBatch()
        {
            var key = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets
                .SetupSequence(s => s.GetSecretsAsync(It.IsAny<List<string>>(), TestContext.Current.CancellationToken))
                .ReturnsAsync(new Dictionary<string, string> {[key] = "alpha"})
                .ReturnsAsync(new Dictionary<string, string> {[key] = "beta"});

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 1
                }));

            Assert.Equal("alpha",
                (await cache.GetSecretsAsync([key], cancellationToken: TestContext.Current.CancellationToken))[key]);

            await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);

            Assert.Equal("beta",
                (await cache.GetSecretsAsync([key], force: true,
                    cancellationToken: TestContext.Current.CancellationToken))[key]);

            secrets.Verify(
                s => s.GetSecretsAsync(It.IsAny<List<string>>(), TestContext.Current.CancellationToken),
                Times.Exactly(2));
        }

        [Fact]
        public async Task Force_WhileCooldownActive_DoesNotCallUnderlyingService()
        {
            var key = Guid.NewGuid().ToString("N");
            var value = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets
                .Setup(s => s.GetSecretsAsync(It.IsAny<List<string>>(), TestContext.Current.CancellationToken))
                .ReturnsAsync(new Dictionary<string, string> {[key] = value});

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 90
                }));

            _ = await cache.GetSecretsAsync([key], cancellationToken: TestContext.Current.CancellationToken);
            var forced = await cache.GetSecretsAsync([key], force: true,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(value, forced[key]);
            secrets.Verify(
                s => s.GetSecretsAsync(It.IsAny<List<string>>(), TestContext.Current.CancellationToken),
                Times.Once);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Miss_BatchesSingleCallThenServesFromCache()
        {
            var keyA = Guid.NewGuid().ToString("N");
            var keyB = Guid.NewGuid().ToString("N");
            var payload = new Dictionary<string, string>
            {
                [keyA] = Guid.NewGuid().ToString("N"),
                [keyB] = Guid.NewGuid().ToString("N")
            };

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets
                .Setup(s => s.GetSecretsAsync(It.Is<List<string>>(keys =>
                        keys.OrderBy(k => k).SequenceEqual(new[] {keyA, keyB}.OrderBy(k => k))),
                    TestContext.Current.CancellationToken))
                .ReturnsAsync(payload);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));
            var request = new List<string> {keyA, keyB};

            var first = await cache.GetSecretsAsync(request, cancellationToken: TestContext.Current.CancellationToken);
            var second = await cache.GetSecretsAsync(request, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(payload[keyA], first[keyA]);
            Assert.Equal(payload[keyB], first[keyB]);
            Assert.Equal(first[keyA], second[keyA]);
            Assert.Equal(first[keyB], second[keyB]);
            secrets.Verify(
                s => s.GetSecretsAsync(It.IsAny<List<string>>(), TestContext.Current.CancellationToken),
                Times.Once);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task MixedHitAndMiss_RequestsOnlyUncachedKeys()
        {
            var cachedKey = Guid.NewGuid().ToString("N");
            var missingKey = Guid.NewGuid().ToString("N");
            var cachedValue = Guid.NewGuid().ToString("N");
            var missingValue = Guid.NewGuid().ToString("N");

            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetSecretAsync(cachedKey, TestContext.Current.CancellationToken))
                .ReturnsAsync(cachedValue);
            secrets
                .Setup(s => s.GetSecretsAsync(It.Is<List<string>>(keys =>
                        keys.Single() == missingKey),
                    TestContext.Current.CancellationToken))
                .ReturnsAsync(new Dictionary<string, string> {[missingKey] = missingValue});

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            _ = await cache.GetSecretAsync(cachedKey, cancellationToken: TestContext.Current.CancellationToken);
            var result = await cache.GetSecretsAsync([cachedKey, missingKey],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(cachedValue, result[cachedKey]);
            Assert.Equal(missingValue, result[missingKey]);
            secrets.Verify(s => s.GetSecretAsync(cachedKey, TestContext.Current.CancellationToken), Times.Once);
            secrets.Verify(
                s => s.GetSecretsAsync(It.IsAny<List<string>>(), TestContext.Current.CancellationToken),
                Times.Once);
            secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task PreCancelledToken_ThrowsBeforeServiceCall()
        {
            var secrets = new Mock<ISecretManagerService>(MockBehavior.Strict);

            var cache = new SecretManagerCacheService(secrets.Object,
                Options.Create(new SecretManagerCacheService.ConfigurationModel
                {
                    ForceCooldownSeconds = 30
                }));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                cache.GetSecretsAsync([Guid.NewGuid().ToString("N")],
                    cancellationToken: new CancellationToken(true)));

            secrets.VerifyNoOtherCalls();
        }
    }

    public class Configuration
    {
        [Theory]
        [InlineData(int.MinValue, 1)]
        [InlineData(-1, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(42, 42)]
        public void EffectiveForceCooldownSeconds_FloorsAtOne(int configured, int expected)
        {
            var model = new SecretManagerCacheService.ConfigurationModel
            {
                ForceCooldownSeconds = configured
            };

            Assert.Equal(expected, model.EffectiveForceCooldownSeconds);
        }
    }

    public class CacheEntry
    {
        [Fact]
        public void IsExpired_WhenExpirationIsInTheFuture_IsFalse()
        {
            var entry = new SecretManagerCacheService.CacheEntry(
                "secret",
                DateTimeOffset.UtcNow.AddMinutes(5),
                DateTimeOffset.UtcNow);

            Assert.False(entry.IsExpired);
        }

        [Fact]
        public void IsExpired_WhenExpirationIsInThePast_IsTrue()
        {
            var entry = new SecretManagerCacheService.CacheEntry(
                "secret",
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow.AddMinutes(-2));

            Assert.True(entry.IsExpired);
        }

        [Fact]
        public void IsExpired_WhenExpirationIsNowOrEarlier_IsTrue()
        {
            var now = DateTimeOffset.UtcNow;
            var entry = new SecretManagerCacheService.CacheEntry(
                "secret",
                now,
                now.AddMinutes(-1));

            // IsExpired uses >=, so an expiration of "now" counts as expired.
            Assert.True(entry.IsExpired);
        }

        [Fact]
        public void IsExpired_WhenNoAbsoluteExpiration_IsFalse()
        {
            var entry = new SecretManagerCacheService.CacheEntry(
                "secret",
                null,
                DateTimeOffset.UtcNow.AddHours(-1));

            Assert.False(entry.IsExpired);
        }

        [Fact]
        public void IsWithinForceCooldown_WhenCooldownHasElapsed_IsFalse()
        {
            var entry = new SecretManagerCacheService.CacheEntry(
                "secret",
                null,
                DateTimeOffset.UtcNow.AddSeconds(-60));

            Assert.False(entry.IsWithinForceCooldown(30));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void IsWithinForceCooldown_WhenCooldownSecondsNotPositive_IsFalse(int cooldownSeconds)
        {
            var entry = new SecretManagerCacheService.CacheEntry(
                "secret",
                null,
                DateTimeOffset.UtcNow);

            Assert.False(entry.IsWithinForceCooldown(cooldownSeconds));
        }

        [Fact]
        public void IsWithinForceCooldown_WhenExactlyAtBoundary_IsFalse()
        {
            const int cooldownSeconds = 10;
            var fetchedAt = DateTimeOffset.UtcNow.AddSeconds(-cooldownSeconds);
            var entry = new SecretManagerCacheService.CacheEntry("secret", null, fetchedAt);

            // Uses < FetchedAt.AddSeconds(cooldown), so exactly at the boundary is outside cooldown.
            Assert.False(entry.IsWithinForceCooldown(cooldownSeconds));
        }

        [Fact]
        public void IsWithinForceCooldown_WhenFetchedRecently_IsTrue()
        {
            var entry = new SecretManagerCacheService.CacheEntry(
                "secret",
                null,
                DateTimeOffset.UtcNow);

            Assert.True(entry.IsWithinForceCooldown(30));
        }

        [Fact]
        public void Value_RoundTrips()
        {
            var value = Guid.NewGuid().ToString("N");
            var entry = new SecretManagerCacheService.CacheEntry(value, null, DateTimeOffset.UtcNow);

            Assert.Equal(value, entry.Value);
        }
    }
}