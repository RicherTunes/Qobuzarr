using System;
using System.Collections.Generic;
using Moq;
using NSubstitute;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Localization;
using NzbDrone.Common.Cache;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Qobuzarr.Tests.Fixtures
{
    public abstract class TestFixtureBase : IDisposable
    {
        protected Mock<Logger> MockLogger { get; private set; }
        protected Mock<IHttpClient> MockHttpClient { get; private set; }
        protected Mock<IConfigService> MockConfigService { get; private set; }
        protected Mock<IDiskProvider> MockDiskProvider { get; private set; }
        protected Mock<IRemotePathMappingService> MockRemotePathMappingService { get; private set; }
        protected Mock<ILocalizationService> MockLocalizationService { get; private set; }
        protected ICacheManager MockCacheManager { get; private set; }
        protected ICached<QobuzSession> MockSessionCache { get; private set; }

        protected TestFixtureBase()
        {
            SetupMocks();
        }

        private void SetupMocks()
        {
            MockLogger = new Mock<Logger>();
            MockHttpClient = new Mock<IHttpClient>();
            MockConfigService = new Mock<IConfigService>();
            MockDiskProvider = new Mock<IDiskProvider>();
            MockRemotePathMappingService = new Mock<IRemotePathMappingService>();
            MockLocalizationService = new Mock<ILocalizationService>();
            
            // Use NSubstitute for cache manager to avoid generic method issues
            MockCacheManager = Substitute.For<ICacheManager>();
            MockSessionCache = Substitute.For<ICached<QobuzSession>>();
            
            // Make the cache mock functional - it should store and retrieve values
            var cacheStorage = new Dictionary<string, QobuzSession>();
            MockSessionCache.Find(Arg.Any<string>())
                           .Returns(callInfo => 
                           {
                               var key = callInfo.Arg<string>();
                               return cacheStorage.ContainsKey(key) ? cacheStorage[key] : default(QobuzSession);
                           });
            MockSessionCache.Get(Arg.Any<string>(), Arg.Any<Func<QobuzSession>>(), Arg.Any<TimeSpan?>())
                           .Returns(callInfo => 
                           {
                               var key = callInfo.Arg<string>();
                               var func = callInfo.Arg<Func<QobuzSession>>();
                               if (cacheStorage.ContainsKey(key)) return cacheStorage[key];
                               if (func != null) 
                               {
                                   var value = func();
                                   cacheStorage[key] = value;
                                   return value;
                               }
                               return default(QobuzSession);
                           });
            MockSessionCache.When(x => x.Set(Arg.Any<string>(), Arg.Any<QobuzSession>(), Arg.Any<TimeSpan?>()))
                           .Do(callInfo => 
                           {
                               var key = callInfo.Arg<string>();
                               var value = callInfo.Arg<QobuzSession>();
                               cacheStorage[key] = value;
                           });
            MockSessionCache.When(x => x.Remove(Arg.Any<string>()))
                           .Do(callInfo => 
                           {
                               var key = callInfo.Arg<string>();
                               cacheStorage.Remove(key);
                           });
            
            // Setup cache manager to return our mock cache
            MockCacheManager.GetCache<QobuzSession>(Arg.Any<Type>())
                           .Returns(MockSessionCache);
            
            // Setup cache manager for object types (used by API client) 
            var mockObjectCache = Substitute.For<ICached<object>>();
            var objectCacheStorage = new Dictionary<string, object>();
            mockObjectCache.Find(Arg.Any<string>())
                          .Returns(callInfo => 
                          {
                              var key = callInfo.Arg<string>();
                              return objectCacheStorage.ContainsKey(key) ? objectCacheStorage[key] : null;
                          });
            mockObjectCache.When(x => x.Set(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<TimeSpan?>()))
                          .Do(callInfo => 
                          {
                              var key = callInfo.Arg<string>();
                              var value = callInfo.Arg<object>();
                              objectCacheStorage[key] = value;
                          });
            mockObjectCache.When(x => x.Remove(Arg.Any<string>()))
                          .Do(callInfo => 
                          {
                              var key = callInfo.Arg<string>();
                              objectCacheStorage.Remove(key);
                          });
            MockCacheManager.GetCache<object>(Arg.Any<Type>())
                           .Returns(mockObjectCache);
        }

        protected void VerifyAllMocks()
        {
            MockLogger.VerifyAll();
            MockHttpClient.VerifyAll();
            MockConfigService.VerifyAll();
            MockDiskProvider.VerifyAll();
            MockRemotePathMappingService.VerifyAll();
            MockLocalizationService.VerifyAll();
            // MockCacheManager is now NSubstitute, doesn't need VerifyAll
        }

        public virtual void Dispose()
        {
            // Cleanup if needed
        }
    }
}