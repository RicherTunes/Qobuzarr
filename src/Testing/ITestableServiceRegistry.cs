using System;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Testing
{
    /// <summary>
    /// Registry interface for testable service configuration.
    /// Provides a standardized way to configure services for testing scenarios.
    /// </summary>
    public interface ITestableServiceRegistry
    {
        /// <summary>
        /// Registers a service mock for testing.
        /// </summary>
        /// <typeparam name="TService">Service interface type</typeparam>
        /// <param name="mockInstance">Mock implementation</param>
        void RegisterMock<TService>(TService mockInstance) where TService : class;

        /// <summary>
        /// Gets a service instance, either mock or real implementation.
        /// </summary>
        /// <typeparam name="TService">Service type to resolve</typeparam>
        /// <returns>Service instance</returns>
        TService GetService<TService>() where TService : class;

        /// <summary>
        /// Checks if a mock is registered for the given service type.
        /// </summary>
        /// <typeparam name="TService">Service type to check</typeparam>
        /// <returns>True if mock is registered</returns>
        bool HasMock<TService>() where TService : class;

        /// <summary>
        /// Clears all registered mocks.
        /// </summary>
        void ClearMocks();
    }

    /// <summary>
    /// Simple implementation of testable service registry.
    /// </summary>
    public class TestableServiceRegistry : ITestableServiceRegistry
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _mocks;
        private readonly IQobuzLogger _logger;

        public TestableServiceRegistry(IQobuzLogger logger = null)
        {
            _mocks = new System.Collections.Concurrent.ConcurrentDictionary<Type, object>();
            _logger = logger;
        }

        public void RegisterMock<TService>(TService mockInstance) where TService : class
        {
            if (mockInstance == null)
                throw new ArgumentNullException(nameof(mockInstance));

            _mocks.AddOrUpdate(typeof(TService), mockInstance, (key, existing) => mockInstance);
            _logger?.Debug("Registered mock for {0}", typeof(TService).Name);
        }

        public TService GetService<TService>() where TService : class
        {
            if (_mocks.TryGetValue(typeof(TService), out var mock))
            {
                return (TService)mock;
            }

            // If no mock is registered, return null - caller should handle
            _logger?.Debug("No mock registered for {0}", typeof(TService).Name);
            return null;
        }

        public bool HasMock<TService>() where TService : class
        {
            return _mocks.ContainsKey(typeof(TService));
        }

        public void ClearMocks()
        {
            _mocks.Clear();
            _logger?.Debug("All mocks cleared");
        }
    }
}
