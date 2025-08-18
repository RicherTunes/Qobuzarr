using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.DependencyInjection
{
    /// <summary>
    /// Simple service container interface for dependency injection in the plugin.
    /// Provides basic DI capabilities for improved testability and loose coupling.
    /// </summary>
    public interface IServiceContainer
    {
        /// <summary>
        /// Registers a service implementation for the specified interface type.
        /// </summary>
        /// <typeparam name="TInterface">The interface type to register</typeparam>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        /// <param name="lifetime">Service lifetime (singleton, transient, etc.)</param>
        void Register<TInterface, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Transient)
            where TImplementation : class, TInterface;

        /// <summary>
        /// Registers a service instance as a singleton.
        /// </summary>
        /// <typeparam name="TInterface">The interface type</typeparam>
        /// <param name="instance">The service instance</param>
        void RegisterInstance<TInterface>(TInterface instance) where TInterface : class;

        /// <summary>
        /// Registers a factory function for creating service instances.
        /// </summary>
        /// <typeparam name="TInterface">The interface type</typeparam>
        /// <param name="factory">Factory function to create instances</param>
        /// <param name="lifetime">Service lifetime</param>
        void RegisterFactory<TInterface>(Func<IServiceContainer, TInterface> factory, ServiceLifetime lifetime = ServiceLifetime.Transient)
            where TInterface : class;

        /// <summary>
        /// Resolves a service of the specified type.
        /// </summary>
        /// <typeparam name="T">The service type to resolve</typeparam>
        /// <returns>Service instance</returns>
        /// <exception cref="InvalidOperationException">Thrown when service cannot be resolved</exception>
        T Resolve<T>() where T : class;

        /// <summary>
        /// Attempts to resolve a service of the specified type.
        /// </summary>
        /// <typeparam name="T">The service type to resolve</typeparam>
        /// <returns>Service instance or null if not registered</returns>
        T TryResolve<T>() where T : class;

        /// <summary>
        /// Checks if a service of the specified type is registered.
        /// </summary>
        /// <typeparam name="T">The service type to check</typeparam>
        /// <returns>True if registered, false otherwise</returns>
        bool IsRegistered<T>() where T : class;

        /// <summary>
        /// Creates a child container that inherits from this container.
        /// Useful for scoped service registration.
        /// </summary>
        /// <returns>Child service container</returns>
        IServiceContainer CreateChild();

        /// <summary>
        /// Releases resources and clears all registrations.
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Service lifetime enumeration for controlling instance creation and disposal.
    /// </summary>
    public enum ServiceLifetime
    {
        /// <summary>
        /// New instance created every time the service is resolved.
        /// </summary>
        Transient,

        /// <summary>
        /// Single instance shared across all resolutions.
        /// </summary>
        Singleton,

        /// <summary>
        /// Single instance per container scope (child containers).
        /// </summary>
        Scoped
    }

    /// <summary>
    /// Service registration information.
    /// </summary>
    public class ServiceRegistration
    {
        public Type ServiceType { get; set; }
        public Type ImplementationType { get; set; }
        public Func<IServiceContainer, object> Factory { get; set; }
        public object Instance { get; set; }
        public ServiceLifetime Lifetime { get; set; }
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    }
}