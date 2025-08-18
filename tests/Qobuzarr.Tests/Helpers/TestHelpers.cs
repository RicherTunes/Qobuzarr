using System;
using System.Collections.Generic;
using Moq;
using NzbDrone.Common.Cache;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Qobuzarr.Tests.Helpers
{
    /// <summary>
    /// Static helper class for creating common test objects and mocks
    /// </summary>
    public static class TestHelpers
    {
        /// <summary>
        /// Creates a properly configured mock cache manager for authentication services
        /// </summary>
        public static Mock<ICacheManager> CreateMockCacheManager()
        {
            var mockCacheManager = new Mock<ICacheManager>();
            
            // Setup cache manager to return a working cache for QobuzSession
            var mockSessionCache = new Mock<ICached<QobuzSession>>();
            var cacheStorage = new Dictionary<string, QobuzSession>();
            
            mockSessionCache.Setup(x => x.Find(It.IsAny<string>()))
                           .Returns<string>(key => cacheStorage.ContainsKey(key) ? cacheStorage[key] : null);
            
            mockSessionCache.Setup(x => x.Set(It.IsAny<string>(), It.IsAny<QobuzSession>(), It.IsAny<TimeSpan?>()))
                           .Callback<string, QobuzSession, TimeSpan?>((key, value, lifetime) => cacheStorage[key] = value);
            
            mockSessionCache.Setup(x => x.Remove(It.IsAny<string>()))
                           .Callback<string>(key => cacheStorage.Remove(key));
            
            mockCacheManager.Setup(x => x.GetCache<QobuzSession>(It.IsAny<Type>()))
                            .Returns(mockSessionCache.Object);

            return mockCacheManager;
        }

        /// <summary>
        /// Creates a test QobuzSession with reasonable defaults
        /// </summary>
        public static QobuzSession CreateTestSession(
            string userId = "test_user_123",
            string authToken = "test_auth_token",
            string appId = "test_app_id",
            string appSecret = "test_app_secret")
        {
            return QobuzSession.CreateSession(userId, authToken, appId, appSecret);
        }

        /// <summary>
        /// Creates test credentials with valid format
        /// </summary>
        public static QobuzCredentials CreateTestCredentials(
            string email = "test@example.com",
            string password = "test_password_hash",
            string appId = "test_app_id",
            string appSecret = "test_app_secret")
        {
            return new QobuzCredentials
            {
                Email = email,
                MD5Password = password,
                AppId = appId,
                AppSecret = appSecret
            };
        }
    }
}