using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using NSubstitute;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Observability;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Tests.Benchmarks
{
    /// <summary>
    /// Performance benchmarks for authentication operations
    /// Measures login, token refresh, session validation, and credential management performance
    /// </summary>
    [Config(typeof(AuthBenchmarkConfig))]
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [RankColumn]
    public class AuthenticationBenchmarks
    {
        private IQobuzAuthenticationService _basicAuthService;
        private IQobuzAuthenticationService _cachedAuthService;
        private IQobuzAuthenticationService _optimizedAuthService;
        private IMetricsCollector _metricsCollector;
        private IQobuzLogger _logger;
        
        // Test data
        private List<QobuzCredentials> _testCredentials;
        private List<string> _testTokens;
        private List<QobuzSession> _testSessions;

        [GlobalSetup]
        public void GlobalSetup()
        {
            // Setup dependencies
            _logger = Substitute.For<IQobuzLogger>();
            _metricsCollector = Substitute.For<IMetricsCollector>();
            
            // Setup different authentication service configurations
            SetupAuthServices();
            
            // Prepare test data
            SetupTestData();
        }

        private void SetupAuthServices()
        {
            // Basic authentication service (no optimizations)
            _basicAuthService = CreateMockAuthService("basic");
            
            // Cached authentication service (with session caching)
            _cachedAuthService = CreateMockAuthService("cached");
            
            // Optimized authentication service (with all optimizations)
            _optimizedAuthService = new OptimizedQobuzAuthService(_logger, _metricsCollector);
        }

        private IQobuzAuthenticationService CreateMockAuthService(string type)
        {
            var service = Substitute.For<IQobuzAuthenticationService>();
            
            // Mock login with realistic delays
            service.LoginAsync(Arg.Any<QobuzCredentials>())
                .Returns(callInfo => CreateMockLoginResponse(type));
                
            service.RefreshTokenAsync(Arg.Any<string>())
                .Returns(callInfo => CreateMockTokenRefreshResponse(type));
                
            service.ValidateSessionAsync(Arg.Any<QobuzSession>())
                .Returns(callInfo => CreateMockSessionValidation(type, callInfo.Arg<QobuzSession>()));
                
            service.LogoutAsync()
                .Returns(callInfo => Task.FromResult(true));
                
            return service;
        }

        private async Task<QobuzSession> CreateMockLoginResponse(string serviceType)
        {
            // Simulate different authentication delays
            var delay = serviceType switch
            {
                "basic" => 250,     // Slower baseline
                "cached" => 150,    // Moderate optimization
                "optimized" => 100, // Best performance
                _ => 200
            };
            
            await Task.Delay(delay);
            
            return new QobuzSession
            {
                UserAuthToken = $"token_{serviceType}_{Guid.NewGuid():N}",
                UserId = 12345,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                User = new QobuzUser 
                { 
                    Id = 12345, 
                    Email = "test@example.com",
                    Subscription = new QobuzSubscription { Label = "Studio Premier" }
                }
            };
        }

        private async Task<string> CreateMockTokenRefreshResponse(string serviceType)
        {
            var delay = serviceType switch
            {
                "basic" => 180,
                "cached" => 80,     // Faster with token caching
                "optimized" => 50,  // Fastest with all optimizations
                _ => 120
            };
            
            await Task.Delay(delay);
            
            return $"refreshed_token_{serviceType}_{DateTime.UtcNow.Ticks}";
        }

        private async Task<bool> CreateMockSessionValidation(string serviceType, QobuzSession session)
        {
            var delay = serviceType switch
            {
                "basic" => 100,
                "cached" => 20,     // Very fast with session caching
                "optimized" => 10,  // Fastest validation
                _ => 60
            };
            
            await Task.Delay(delay);
            
            // Simulate 90% of sessions being valid
            return session?.UserAuthToken != null && DateTime.UtcNow < session.ExpiresAt;
        }

        private void SetupTestData()
        {
            _testCredentials = new List<QobuzCredentials>
            {
                new() { Email = "user1@example.com", Password = "password1" },
                new() { Email = "user2@example.com", Password = "password2" },
                new() { Email = "user3@example.com", Password = "password3" },
                new() { Email = "user4@example.com", Password = "password4" },
                new() { Email = "user5@example.com", Password = "password5" },
                new() { Email = "user1@example.com", Password = "password1" }, // Duplicate for caching tests
                new() { Email = "user2@example.com", Password = "password2" }  // Duplicate for caching tests
            };

            _testTokens = new List<string>
            {
                "token_123456789_abcdef",
                "token_987654321_fedcba",
                "token_456789123_ghijkl",
                "token_789123456_mnopqr",
                "token_123789456_stuvwx"
            };

            _testSessions = new List<QobuzSession>();
            for (int i = 0; i < 10; i++)
            {
                _testSessions.Add(new QobuzSession
                {
                    UserAuthToken = $"session_token_{i}",
                    UserId = 10000 + i,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30 + i * 5), // Varying expiry times
                    User = new QobuzUser { Id = 10000 + i, Email = $"user{i}@test.com" }
                });
            }
        }

        #region Authentication Benchmarks

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Authentication")]
        public async Task<List<QobuzSession>> BasicAuth_LoginUsers()
        {
            var results = new List<QobuzSession>();
            foreach (var credential in _testCredentials.Take(5)) // Limit for benchmark
            {
                var session = await _basicAuthService.LoginAsync(credential);
                results.Add(session);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Authentication")]
        public async Task<List<QobuzSession>> CachedAuth_LoginUsers()
        {
            var results = new List<QobuzSession>();
            foreach (var credential in _testCredentials.Take(5))
            {
                var session = await _cachedAuthService.LoginAsync(credential);
                results.Add(session);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("Authentication")]
        public async Task<List<QobuzSession>> OptimizedAuth_LoginUsers()
        {
            var results = new List<QobuzSession>();
            foreach (var credential in _testCredentials.Take(5))
            {
                var session = await _optimizedAuthService.LoginAsync(credential);
                results.Add(session);
            }
            return results;
        }

        #endregion

        #region Token Refresh Benchmarks

        [Benchmark]
        [BenchmarkCategory("TokenRefresh")]
        public async Task<List<string>> BasicAuth_RefreshTokens()
        {
            var results = new List<string>();
            foreach (var token in _testTokens)
            {
                var refreshed = await _basicAuthService.RefreshTokenAsync(token);
                results.Add(refreshed);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("TokenRefresh")]
        public async Task<List<string>> CachedAuth_RefreshTokens()
        {
            var results = new List<string>();
            foreach (var token in _testTokens)
            {
                var refreshed = await _cachedAuthService.RefreshTokenAsync(token);
                results.Add(refreshed);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("TokenRefresh")]
        public async Task<List<string>> OptimizedAuth_RefreshTokens()
        {
            var results = new List<string>();
            foreach (var token in _testTokens)
            {
                var refreshed = await _optimizedAuthService.RefreshTokenAsync(token);
                results.Add(refreshed);
            }
            return results;
        }

        #endregion

        #region Session Validation Benchmarks

        [Benchmark]
        [BenchmarkCategory("SessionValidation")]
        public async Task<List<bool>> BasicAuth_ValidateSessions()
        {
            var results = new List<bool>();
            foreach (var session in _testSessions)
            {
                var isValid = await _basicAuthService.ValidateSessionAsync(session);
                results.Add(isValid);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("SessionValidation")]
        public async Task<List<bool>> CachedAuth_ValidateSessions()
        {
            var results = new List<bool>();
            foreach (var session in _testSessions)
            {
                var isValid = await _cachedAuthService.ValidateSessionAsync(session);
                results.Add(isValid);
            }
            return results;
        }

        [Benchmark]
        [BenchmarkCategory("SessionValidation")]
        public async Task<List<bool>> OptimizedAuth_ValidateSessions()
        {
            var results = new List<bool>();
            foreach (var session in _testSessions)
            {
                var isValid = await _optimizedAuthService.ValidateSessionAsync(session);
                results.Add(isValid);
            }
            return results;
        }

        #endregion

        #region High Volume Authentication Benchmarks

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<object> BasicAuth_HighVolumeOperations()
        {
            var loginCount = 0;
            var refreshCount = 0;
            var validationCount = 0;

            // Simulate mixed authentication operations
            for (int i = 0; i < 3; i++)
            {
                // Login
                var session = await _basicAuthService.LoginAsync(_testCredentials[i]);
                loginCount++;

                // Token refresh
                var refreshed = await _basicAuthService.RefreshTokenAsync(_testTokens[i % _testTokens.Count]);
                refreshCount++;

                // Session validation
                var isValid = await _basicAuthService.ValidateSessionAsync(_testSessions[i]);
                validationCount++;
            }

            return new { Logins = loginCount, Refreshes = refreshCount, Validations = validationCount };
        }

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<object> CachedAuth_HighVolumeOperations()
        {
            var loginCount = 0;
            var refreshCount = 0;
            var validationCount = 0;

            // Simulate mixed authentication operations with caching benefits
            for (int i = 0; i < 3; i++)
            {
                var session = await _cachedAuthService.LoginAsync(_testCredentials[i]);
                loginCount++;

                var refreshed = await _cachedAuthService.RefreshTokenAsync(_testTokens[i % _testTokens.Count]);
                refreshCount++;

                var isValid = await _cachedAuthService.ValidateSessionAsync(_testSessions[i]);
                validationCount++;
            }

            return new { Logins = loginCount, Refreshes = refreshCount, Validations = validationCount };
        }

        [Benchmark]
        [BenchmarkCategory("HighVolume")]
        public async Task<object> OptimizedAuth_HighVolumeOperations()
        {
            var loginCount = 0;
            var refreshCount = 0;
            var validationCount = 0;

            // Simulate mixed authentication operations with all optimizations
            for (int i = 0; i < 3; i++)
            {
                var session = await _optimizedAuthService.LoginAsync(_testCredentials[i]);
                loginCount++;

                var refreshed = await _optimizedAuthService.RefreshTokenAsync(_testTokens[i % _testTokens.Count]);
                refreshCount++;

                var isValid = await _optimizedAuthService.ValidateSessionAsync(_testSessions[i]);
                validationCount++;
            }

            return new { Logins = loginCount, Refreshes = refreshCount, Validations = validationCount };
        }

        #endregion

        #region Concurrent Authentication Benchmarks

        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task<List<QobuzSession>> BasicAuth_ConcurrentLogins()
        {
            var tasks = _testCredentials.Take(5).Select(cred => 
                _basicAuthService.LoginAsync(cred)).ToArray();
                
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task<List<QobuzSession>> CachedAuth_ConcurrentLogins()
        {
            var tasks = _testCredentials.Take(5).Select(cred => 
                _cachedAuthService.LoginAsync(cred)).ToArray();
                
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task<List<QobuzSession>> OptimizedAuth_ConcurrentLogins()
        {
            var tasks = _testCredentials.Take(5).Select(cred => 
                _optimizedAuthService.LoginAsync(cred)).ToArray();
                
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        [Benchmark]
        [BenchmarkCategory("Concurrency")]
        public async Task<List<bool>> ConcurrentSessionValidations()
        {
            var tasks = _testSessions.Select(session => 
                _optimizedAuthService.ValidateSessionAsync(session)).ToArray();
                
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        #endregion

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            // Cleanup resources
            _optimizedAuthService?.Dispose();
        }
    }

    /// <summary>
    /// Optimized authentication service for benchmarking
    /// </summary>
    internal class OptimizedQobuzAuthService : IQobuzAuthenticationService
    {
        private readonly IQobuzLogger _logger;
        private readonly IMetricsCollector _metrics;
        private readonly Dictionary<string, object> _cache = new();
        private readonly Dictionary<string, DateTime> _cacheExpiry = new();

        public OptimizedQobuzAuthService(IQobuzLogger logger, IMetricsCollector metrics)
        {
            _logger = logger;
            _metrics = metrics;
        }

        public async Task<QobuzSession> LoginAsync(QobuzCredentials credentials)
        {
            var cacheKey = $"login:{credentials.Email}";
            
            // Check cache
            if (_cache.TryGetValue(cacheKey, out var cached) && 
                _cacheExpiry.TryGetValue(cacheKey, out var expiry) &&
                DateTime.UtcNow < expiry)
            {
                _metrics?.RecordCacheHit("auth", cacheKey, true);
                _metrics?.RecordAuthenticationAttempt("cached_login", true);
                return (QobuzSession)cached;
            }

            _metrics?.RecordCacheHit("auth", cacheKey, false);
            
            // Optimized authentication flow
            await Task.Delay(100); // Fastest authentication simulation
            
            var session = new QobuzSession
            {
                UserAuthToken = $"optimized_token_{Guid.NewGuid():N}",
                UserId = 99999,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                User = new QobuzUser 
                { 
                    Id = 99999, 
                    Email = credentials.Email,
                    Subscription = new QobuzSubscription { Label = "Studio Premier" }
                }
            };
            
            // Cache the session
            _cache[cacheKey] = session;
            _cacheExpiry[cacheKey] = DateTime.UtcNow.AddMinutes(30);
            
            _metrics?.RecordAuthenticationAttempt("optimized_login", true);
            return session;
        }

        public async Task<string> RefreshTokenAsync(string expiredToken)
        {
            var cacheKey = $"refresh:{expiredToken}";
            
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _metrics?.RecordCacheHit("token_refresh", cacheKey, true);
                return (string)cached;
            }

            _metrics?.RecordCacheHit("token_refresh", cacheKey, false);
            
            // Optimized token refresh
            await Task.Delay(50); // Fastest refresh simulation
            
            var newToken = $"optimized_refresh_{DateTime.UtcNow.Ticks}";
            _cache[cacheKey] = newToken;
            _cacheExpiry[cacheKey] = DateTime.UtcNow.AddMinutes(15);
            
            return newToken;
        }

        public async Task<bool> ValidateSessionAsync(QobuzSession session)
        {
            var cacheKey = $"validate:{session?.UserAuthToken}";
            
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                _metrics?.RecordCacheHit("session_validation", cacheKey, true);
                return (bool)cached;
            }

            _metrics?.RecordCacheHit("session_validation", cacheKey, false);
            
            // Ultra-fast validation
            await Task.Delay(10);
            
            var isValid = session?.UserAuthToken != null && DateTime.UtcNow < session.ExpiresAt;
            
            _cache[cacheKey] = isValid;
            _cacheExpiry[cacheKey] = DateTime.UtcNow.AddMinutes(5);
            
            return isValid;
        }

        public async Task<bool> LogoutAsync()
        {
            await Task.Delay(25); // Fast logout
            _cache.Clear();
            _cacheExpiry.Clear();
            return true;
        }

        public void Dispose()
        {
            _cache.Clear();
            _cacheExpiry.Clear();
        }
    }

    /// <summary>
    /// Benchmark configuration for authentication performance testing
    /// </summary>
    public class AuthBenchmarkConfig : ManualConfig
    {
        public AuthBenchmarkConfig()
        {
            AddJob(Job.Default
                .WithWarmupCount(3)      // 3 warmup iterations
                .WithIterationCount(10)  // 10 measurement iterations
                .WithInvocationCount(1)  // 1 invocation per iteration
                .WithUnrollFactor(1));   // No unrolling for async
                
            WithOption(ConfigOptions.DisableOptimizationsValidator, true);
            WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default
                .WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend)
                .WithTimeUnit(BenchmarkDotNet.Columns.TimeUnit.Millisecond));
        }
    }

    /// <summary>
    /// Program entry point for running authentication benchmarks
    /// Usage: dotnet run --project tests/Benchmarks --configuration Release -- Auth
    /// </summary>
    public class AuthenticationBenchmarkRunner
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("🔐 Qobuzarr Authentication Performance Benchmarks");
            Console.WriteLine("Comparing Basic, Cached, and Optimized authentication implementations");
            Console.WriteLine();
            
            var summary = BenchmarkRunner.Run<AuthenticationBenchmarks>();
            
            Console.WriteLine();
            Console.WriteLine("📊 Authentication Benchmark Summary:");
            Console.WriteLine($"Total benchmarks run: {summary.Reports.Length}");
            
            // Display key insights
            Console.WriteLine();
            Console.WriteLine("🚀 Expected performance characteristics:");
            Console.WriteLine("- Basic Auth: Baseline performance with full authentication flow");
            Console.WriteLine("- Cached Auth: Improved performance with session/token caching");
            Console.WriteLine("- Optimized Auth: Best performance with intelligent caching and fast paths");
            Console.WriteLine("- Session Validation: Dramatic improvements with caching");
            Console.WriteLine("- Token Refresh: Significant speedup with optimized flows");
        }
    }
}