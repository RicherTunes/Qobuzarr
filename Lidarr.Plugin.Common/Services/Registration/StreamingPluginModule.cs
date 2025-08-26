using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lidarr.Plugin.Common.Base;
using Lidarr.Plugin.Common.Interfaces;

namespace Lidarr.Plugin.Common.Services.Registration
{
    /// <summary>
    /// Base class for streaming service plugin modules.
    /// Provides standard patterns for plugin registration and dependency injection setup.
    /// </summary>
    public abstract class StreamingPluginModule
    {
        private readonly Dictionary<Type, object> _singletonInstances = new Dictionary<Type, object>();
        private readonly object _lock = new object();

        /// <summary>
        /// Gets the name of this streaming service plugin.
        /// </summary>
        public abstract string ServiceName { get; }

        /// <summary>
        /// Gets the version of this plugin.
        /// </summary>
        public virtual string Version => GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0.0";

        /// <summary>
        /// Gets the description of this plugin.
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Gets the author of this plugin.
        /// </summary>
        public abstract string Author { get; }

        /// <summary>
        /// Registers all services required by this streaming plugin.
        /// This method is called by Lidarr during plugin initialization.
        /// </summary>
        public virtual void RegisterServices()
        {
            RegisterCoreServices();
            RegisterAuthenticationServices();
            RegisterHttpServices();
            RegisterCachingServices();
            RegisterQualityServices();
            RegisterDownloadServices();
            RegisterCustomServices();
        }

        /// <summary>
        /// Registers core plugin services (indexer, download client, settings).
        /// Must be implemented by derived classes.
        /// </summary>
        protected abstract void RegisterCoreServices();

        /// <summary>
        /// Registers authentication-related services.
        /// Override to provide custom authentication service implementations.
        /// </summary>
        protected virtual void RegisterAuthenticationServices()
        {
            // Default implementation - derived classes can override
        }

        /// <summary>
        /// Registers HTTP and API client services.
        /// Override to provide custom HTTP client implementations.
        /// </summary>
        protected virtual void RegisterHttpServices()
        {
            // Default implementation - derived classes can override
        }

        /// <summary>
        /// Registers caching services.
        /// Override to provide custom caching implementations.
        /// </summary>
        protected virtual void RegisterCachingServices()
        {
            // Default implementation - derived classes can override
        }

        /// <summary>
        /// Registers quality management services.
        /// Override to provide custom quality mapping implementations.
        /// </summary>
        protected virtual void RegisterQualityServices()
        {
            // Default implementation - derived classes can override
        }

        /// <summary>
        /// Registers download-related services.
        /// Override to provide custom download service implementations.
        /// </summary>
        protected virtual void RegisterDownloadServices()
        {
            // Default implementation - derived classes can override
        }

        /// <summary>
        /// Registers custom services specific to this streaming service.
        /// Override to register additional service-specific components.
        /// </summary>
        protected virtual void RegisterCustomServices()
        {
            // Default implementation - derived classes can override
        }

        /// <summary>
        /// Creates or retrieves a singleton instance of the specified type.
        /// </summary>
        protected T GetSingleton<T>(Func<T> factory) where T : class
        {
            return (T)GetSingleton(typeof(T), () => factory());
        }

        /// <summary>
        /// Creates or retrieves a singleton instance of the specified type.
        /// </summary>
        protected object GetSingleton(Type type, Func<object> factory)
        {
            lock (_lock)
            {
                if (!_singletonInstances.TryGetValue(type, out object instance))
                {
                    instance = factory();
                    _singletonInstances[type] = instance;
                }
                return instance;
            }
        }

        /// <summary>
        /// Validates that all required services are properly registered.
        /// </summary>
        public virtual ValidationResult ValidateRegistration()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            try
            {
                ValidateCoreServices(errors, warnings);
                ValidateOptionalServices(errors, warnings);
            }
            catch (Exception ex)
            {
                errors.Add($"Validation failed with exception: {ex.Message}");
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors,
                Warnings = warnings
            };
        }

        /// <summary>
        /// Validates that core required services are registered.
        /// </summary>
        protected virtual void ValidateCoreServices(List<string> errors, List<string> warnings)
        {
            // Override in derived classes to validate service-specific requirements
        }

        /// <summary>
        /// Validates optional services and provides warnings if missing.
        /// </summary>
        protected virtual void ValidateOptionalServices(List<string> errors, List<string> warnings)
        {
            // Override in derived classes to check optional services
        }

        /// <summary>
        /// Gets plugin metadata for display in Lidarr.
        /// </summary>
        public virtual PluginMetadata GetMetadata()
        {
            return new PluginMetadata
            {
                Name = ServiceName,
                Version = Version,
                Description = Description,
                Author = Author,
                AssemblyName = GetType().Assembly.GetName().Name,
                HasIndexer = HasIndexer(),
                HasDownloadClient = HasDownloadClient(),
                SupportedFeatures = GetSupportedFeatures(),
                RequiredSettings = GetRequiredSettings()
            };
        }

        /// <summary>
        /// Determines if this plugin provides an indexer.
        /// </summary>
        protected virtual bool HasIndexer() => true;

        /// <summary>
        /// Determines if this plugin provides a download client.
        /// </summary>
        protected virtual bool HasDownloadClient() => true;

        /// <summary>
        /// Gets the list of features supported by this plugin.
        /// </summary>
        protected virtual List<string> GetSupportedFeatures()
        {
            var features = new List<string>();
            
            if (HasIndexer()) features.Add("Search");
            if (HasDownloadClient()) features.Add("Download");
            if (SupportsCaching()) features.Add("Caching");
            if (SupportsAuthentication()) features.Add("Authentication");
            if (SupportsQualitySelection()) features.Add("Quality Selection");
            
            return features;
        }

        /// <summary>
        /// Gets the list of required settings for this plugin.
        /// </summary>
        protected virtual List<string> GetRequiredSettings()
        {
            return new List<string> { "BaseUrl" };
        }

        /// <summary>
        /// Whether this plugin supports caching.
        /// </summary>
        protected virtual bool SupportsCaching() => true;

        /// <summary>
        /// Whether this plugin supports authentication.
        /// </summary>
        protected virtual bool SupportsAuthentication() => true;

        /// <summary>
        /// Whether this plugin supports quality selection.
        /// </summary>
        protected virtual bool SupportsQualitySelection() => true;

        /// <summary>
        /// Disposes of plugin resources.
        /// </summary>
        public virtual void Dispose()
        {
            lock (_lock)
            {
                foreach (var instance in _singletonInstances.Values)
                {
                    if (instance is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch
                        {
                            // Log error but continue disposing other instances
                        }
                    }
                }
                _singletonInstances.Clear();
            }
        }
    }

    /// <summary>
    /// Plugin metadata for Lidarr display.
    /// </summary>
    public class PluginMetadata
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string AssemblyName { get; set; }
        public bool HasIndexer { get; set; }
        public bool HasDownloadClient { get; set; }
        public List<string> SupportedFeatures { get; set; } = new List<string>();
        public List<string> RequiredSettings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Result of plugin registration validation.
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();

        public override string ToString()
        {
            var result = IsValid ? "Valid" : "Invalid";
            
            if (Errors.Any())
            {
                result += $"\nErrors: {string.Join(", ", Errors)}";
            }
            
            if (Warnings.Any())
            {
                result += $"\nWarnings: {string.Join(", ", Warnings)}";
            }
            
            return result;
        }
    }

    /// <summary>
    /// Helper attribute for marking services that should be auto-registered.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class AutoRegisterServiceAttribute : Attribute
    {
        public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Singleton;
        public Type ServiceType { get; set; }

        public AutoRegisterServiceAttribute() { }

        public AutoRegisterServiceAttribute(Type serviceType)
        {
            ServiceType = serviceType;
        }
    }

    /// <summary>
    /// Service lifetime options for dependency injection.
    /// </summary>
    public enum ServiceLifetime
    {
        Singleton,
        Transient,
        Scoped
    }
}