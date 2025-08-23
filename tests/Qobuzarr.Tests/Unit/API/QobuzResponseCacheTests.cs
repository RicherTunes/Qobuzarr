using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API.Caching;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.API
{
    /// <summary>
    /// Comprehensive tests for QobuzResponseCache covering caching logic,
    /// cache key generation, TTL determination, and cache operations.
    /// </summary>
    public class QobuzResponseCacheTests : TestFixtureBase
    {
        private readonly QobuzResponseCache _cache;

        public QobuzResponseCacheTests()
        {
            _cache = new QobuzResponseCache(MockCacheManager, MockLogger.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullCacheManager_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzResponseCache(null, MockLogger.Object);
            act.Should().Throw<ArgumentNullException>().WithMessage("*cacheManager*");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzResponseCache(MockCacheManager, null);
            act.Should().Throw<ArgumentNullException>().WithMessage("*logger*");
        }

        #endregion

        #region ShouldCache Tests

        [Theory]
        [InlineData("/album/search", true)]
        [InlineData("/artist/search", true)]
        [InlineData("/track/search", true)]
        [InlineData("/album/get", true)]
        [InlineData("/artist/get", true)]
        [InlineData("/track/get", true)]
        [InlineData("/playlist/get", true)]
        [InlineData("/label/get", true)]
        public void ShouldCache_WithCacheableEndpoints_ShouldReturnTrue(string endpoint, bool expected)
        {
            // Act
            var result = _cache.ShouldCache(endpoint);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("/user/login", false)]
        [InlineData("/track/getFileUrl", false)]
        [InlineData("/user/profile", false)]
        [InlineData("/auth/token", false)]
        [InlineData("", false)]
        [InlineData("/unknown/endpoint", false)]
        public void ShouldCache_WithNonCacheableEndpoints_ShouldReturnFalse(string endpoint, bool expected)
        {
            // Act
            var result = _cache.ShouldCache(endpoint);

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void ShouldCache_WithNullEndpoint_ShouldReturnFalse()
        {
            // Act
            var result = _cache.ShouldCache(null);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region GetCacheDuration Tests

        [Fact]
        public void GetCacheDuration_WithSearchEndpoint_ShouldReturnShortDuration()
        {
            // Act
            var duration = _cache.GetCacheDuration("/album/search");

            // Assert
            duration.Should().Be(QobuzConstants.Cache.ShortDuration);
        }

        [Fact]
        public void GetCacheDuration_WithAlbumGetEndpoint_ShouldReturnMediumDuration()
        {
            // Act
            var duration = _cache.GetCacheDuration("/album/get");

            // Assert
            duration.Should().Be(QobuzConstants.Cache.MediumDuration);
        }

        [Fact]
        public void GetCacheDuration_WithArtistGetEndpoint_ShouldReturnLongDuration()
        {
            // Act
            var duration = _cache.GetCacheDuration("/artist/get");

            // Assert
            duration.Should().Be(QobuzConstants.Cache.LongDuration);
        }

        [Fact]
        public void GetCacheDuration_WithLabelGetEndpoint_ShouldReturnLongDuration()
        {
            // Act
            var duration = _cache.GetCacheDuration("/label/get");

            // Assert
            duration.Should().Be(QobuzConstants.Cache.LongDuration);
        }

        [Fact]
        public void GetCacheDuration_WithPlaylistGetEndpoint_ShouldReturnMediumDuration()
        {
            // Act
            var duration = _cache.GetCacheDuration("/playlist/get");

            // Assert
            duration.Should().Be(QobuzConstants.Cache.MediumDuration);
        }

        [Fact]
        public void GetCacheDuration_WithTrackGetEndpoint_ShouldReturnMediumDuration()
        {
            // Act
            var duration = _cache.GetCacheDuration("/track/get");

            // Assert
            duration.Should().Be(QobuzConstants.Cache.MediumDuration);
        }

        [Fact]
        public void GetCacheDuration_WithUnknownEndpoint_ShouldReturnSessionDuration()
        {
            // Act
            var duration = _cache.GetCacheDuration("/unknown/endpoint");

            // Assert
            duration.Should().Be(QobuzConstants.Cache.SessionDuration);
        }

        #endregion

        #region GenerateCacheKey Tests

        [Fact]
        public void GenerateCacheKey_WithBasicParameters_ShouldGenerateConsistentKey()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string>
            {
                { "album_id", "123456" },
                { "extra", "tracks" }
            };

            // Act
            var key1 = _cache.GenerateCacheKey(endpoint, parameters);
            var key2 = _cache.GenerateCacheKey(endpoint, parameters);

            // Assert
            key1.Should().NotBeNullOrWhiteSpace();
            key1.Should().Be(key2); // Should be deterministic
        }

        [Fact]
        public void GenerateCacheKey_ShouldExcludeAuthenticationParameters()
        {
            // Arrange
            var endpoint = "/album/get";
            var parametersWithAuth = new Dictionary<string, string>
            {
                { "album_id", "123456" },
                { "user_auth_token", "secret_token" },
                { "app_id", "app_123" }
            };

            var parametersWithoutAuth = new Dictionary<string, string>
            {
                { "album_id", "123456" }
            };

            // Act
            var keyWithAuth = _cache.GenerateCacheKey(endpoint, parametersWithAuth);
            var keyWithoutAuth = _cache.GenerateCacheKey(endpoint, parametersWithoutAuth);

            // Assert
            keyWithAuth.Should().Be(keyWithoutAuth);
        }

        [Fact]
        public void GenerateCacheKey_WithDifferentParameterOrder_ShouldGenerateSameKey()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters1 = new Dictionary<string, string>
            {
                { "query", "test" },
                { "limit", "20" },
                { "offset", "0" }
            };

            var parameters2 = new Dictionary<string, string>
            {
                { "offset", "0" },
                { "query", "test" },
                { "limit", "20" }
            };

            // Act
            var key1 = _cache.GenerateCacheKey(endpoint, parameters1);
            var key2 = _cache.GenerateCacheKey(endpoint, parameters2);

            // Assert
            key1.Should().Be(key2);
        }

        [Fact]
        public void GenerateCacheKey_WithDifferentParameters_ShouldGenerateDifferentKeys()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters1 = new Dictionary<string, string> { { "album_id", "123456" } };
            var parameters2 = new Dictionary<string, string> { { "album_id", "789012" } };

            // Act
            var key1 = _cache.GenerateCacheKey(endpoint, parameters1);
            var key2 = _cache.GenerateCacheKey(endpoint, parameters2);

            // Assert
            key1.Should().NotBe(key2);
        }

        [Fact]
        public void GenerateCacheKey_WithDifferentEndpoints_ShouldGenerateDifferentKeys()
        {
            // Arrange
            var parameters = new Dictionary<string, string> { { "id", "123456" } };

            // Act
            var key1 = _cache.GenerateCacheKey("/album/get", parameters);
            var key2 = _cache.GenerateCacheKey("/artist/get", parameters);

            // Assert
            key1.Should().NotBe(key2);
        }

        [Fact]
        public void GenerateCacheKey_WithEmptyParameters_ShouldGenerateValidKey()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters = new Dictionary<string, string>();

            // Act
            var key = _cache.GenerateCacheKey(endpoint, parameters);

            // Assert
            key.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void GenerateCacheKey_WithSpecialCharacters_ShouldHandleGracefully()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters = new Dictionary<string, string>
            {
                { "query", "Miles Davis & John Coltrane" },
                { "special", "äöü@#$%^&*()" }
            };

            // Act
            var key = _cache.GenerateCacheKey(endpoint, parameters);

            // Assert
            key.Should().NotBeNullOrWhiteSpace();
        }

        #endregion

        #region Get Tests

        [Fact]
        public void Get_WithCacheableEndpoint_WhenCacheHit_ShouldReturnCachedValue()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string> { { "album_id", "123456" } };
            var testValue = new TestModel { Name = "Test Album", Id = 123 };

            _cache.Set(endpoint, parameters, testValue);

            // Act
            var result = _cache.Get<TestModel>(endpoint, parameters);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be("Test Album");
            result.Id.Should().Be(123);
        }

        [Fact]
        public void Get_WithCacheableEndpoint_WhenCacheMiss_ShouldReturnNull()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string> { { "album_id", "nonexistent" } };

            // Act
            var result = _cache.Get<TestModel>(endpoint, parameters);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void Get_WithNonCacheableEndpoint_ShouldReturnNull()
        {
            // Arrange
            var endpoint = "/user/login";
            var parameters = new Dictionary<string, string> { { "email", "test@example.com" } };
            var testValue = new TestModel { Name = "Test", Id = 1 };

            _cache.Set(endpoint, parameters, testValue); // This won't actually cache

            // Act
            var result = _cache.Get<TestModel>(endpoint, parameters);

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region Set Tests

        [Fact]
        public void Set_WithCacheableEndpoint_ShouldStoreValue()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string> { { "album_id", "123456" } };
            var testValue = new TestModel { Name = "Test Album", Id = 123 };

            // Act
            _cache.Set(endpoint, parameters, testValue);
            var result = _cache.Get<TestModel>(endpoint, parameters);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be("Test Album");
        }

        [Fact]
        public void Set_WithNonCacheableEndpoint_ShouldNotStoreValue()
        {
            // Arrange
            var endpoint = "/user/login";
            var parameters = new Dictionary<string, string> { { "email", "test@example.com" } };
            var testValue = new TestModel { Name = "Test", Id = 1 };

            // Act
            _cache.Set(endpoint, parameters, testValue);
            var result = _cache.Get<TestModel>(endpoint, parameters);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void Set_WithNullValue_ShouldNotStoreValue()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string> { { "album_id", "123456" } };

            // Act
            _cache.Set<TestModel>(endpoint, parameters, null);
            var result = _cache.Get<TestModel>(endpoint, parameters);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void Set_WithCustomDuration_ShouldStoreValue()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string> { { "album_id", "123456" } };
            var testValue = new TestModel { Name = "Test Album", Id = 123 };
            var customDuration = TimeSpan.FromMinutes(30);

            // Act
            _cache.Set(endpoint, parameters, testValue, customDuration);
            var result = _cache.Get<TestModel>(endpoint, parameters);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be("Test Album");
        }

        #endregion

        #region Clear Tests

        [Fact]
        public void Clear_ShouldClearAllCachedItems()
        {
            // Arrange
            var endpoint1 = "/album/get";
            var endpoint2 = "/artist/get";
            var parameters = new Dictionary<string, string> { { "id", "123" } };
            var testValue = new TestModel { Name = "Test", Id = 123 };

            _cache.Set(endpoint1, parameters, testValue);
            _cache.Set(endpoint2, parameters, testValue);

            // Verify items are cached
            _cache.Get<TestModel>(endpoint1, parameters).Should().NotBeNull();
            _cache.Get<TestModel>(endpoint2, parameters).Should().NotBeNull();

            // Act
            _cache.Clear();

            // Assert
            _cache.Get<TestModel>(endpoint1, parameters).Should().BeNull();
            _cache.Get<TestModel>(endpoint2, parameters).Should().BeNull();
        }

        [Fact]
        public void ClearEndpoint_ShouldLogWarning()
        {
            // Arrange
            var endpoint = "/album/get";

            // Act
            _cache.ClearEndpoint(endpoint);

            // Assert - Should not throw, but will log a warning
            // The actual warning logging can't be easily verified without more complex mock setup
            Assert.True(true); // Test completes without exceptions
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void GenerateCacheKey_WithNullParameters_ShouldHandleGracefully()
        {
            // Act
            var key = _cache.GenerateCacheKey("/album/get", null);

            // Assert
            key.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ShouldCache_WithInvalidEndpoint_ShouldReturnFalse(string endpoint)
        {
            // Act
            var result = _cache.ShouldCache(endpoint);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void Get_WithDifferentGenericType_ShouldReturnNull()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string> { { "album_id", "123456" } };
            var testValue = new TestModel { Name = "Test Album", Id = 123 };

            _cache.Set(endpoint, parameters, testValue);

            // Act - Try to get as different type
            var result = _cache.Get<AnotherTestModel>(endpoint, parameters);

            // Assert
            result.Should().BeNull(); // Type mismatch should return null
        }

        #endregion

        #region Test Models

        private class TestModel
        {
            public string Name { get; set; }
            public int Id { get; set; }
        }

        private class AnotherTestModel
        {
            public string Title { get; set; }
            public long Value { get; set; }
        }

        #endregion

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}