using System;
using System.Linq;
using System.Reflection;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Testing
{
    /// <summary>
    /// Extension methods to improve testability of existing services.
    /// Provides utilities for dependency injection and test configuration.
    /// </summary>
    public static class TestabilityExtensions
    {
        /// <summary>
        /// Creates a testable version of a service by replacing dependencies with mocks.
        /// Uses reflection to identify and replace constructor parameters.
        /// </summary>
        /// <typeparam name="TService">Service type to make testable</typeparam>
        /// <param name="registry">Mock registry containing test doubles</param>
        /// <param name="fallbackFactory">Factory for creating real dependencies when no mock exists</param>
        /// <returns>Service instance with dependencies resolved from mocks or factory</returns>
        public static TService CreateTestableInstance<TService>(
            this ITestableServiceRegistry registry,
            Func<Type, object> fallbackFactory = null)
            where TService : class
        {
            var serviceType = typeof(TService);
            var constructors = serviceType.GetConstructors();

            // Find the constructor with the most parameters (likely the DI constructor)
            ConstructorInfo? bestConstructor = null;
            int maxParameters = -1;

            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length > maxParameters)
                {
                    maxParameters = parameters.Length;
                    bestConstructor = constructor;
                }
            }

            if (bestConstructor == null)
            {
                throw new InvalidOperationException($"No suitable constructor found for {serviceType.Name}");
            }

            // Resolve constructor parameters
            var constructorParams = bestConstructor.GetParameters();
            var args = new object[constructorParams.Length];

            for (int i = 0; i < constructorParams.Length; i++)
            {
                var paramType = constructorParams[i].ParameterType;
                var mockInstance = GetMockInstance(registry, paramType);

                if (mockInstance != null)
                {
                    args[i] = mockInstance;
                }
                else if (fallbackFactory != null)
                {
                    args[i] = fallbackFactory(paramType);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Cannot resolve parameter {constructorParams[i].Name} of type {paramType.Name} for {serviceType.Name}. " +
                        "Register a mock or provide a fallback factory.");
                }
            }

            return (TService)Activator.CreateInstance(serviceType, args)!;
        }

        /// <summary>
        /// Validates that a service follows testable design patterns.
        /// </summary>
        /// <typeparam name="TService">Service type to validate</typeparam>
        /// <returns>Validation result with recommendations</returns>
        public static TestabilityValidationResult ValidateTestability<TService>()
            where TService : class
        {
            var result = new TestabilityValidationResult();
            var serviceType = typeof(TService);

            // Check for dependency injection constructor
            var constructors = serviceType.GetConstructors();
            var diConstructor = Array.Find(constructors, c =>
                c.GetParameters().Length > 0 &&
                Array.TrueForAll(c.GetParameters(), p => p.ParameterType.IsInterface));

            if (diConstructor == null)
            {
                result.AddIssue(TestabilityIssueLevel.Major,
                    "No dependency injection constructor found",
                    "Add a constructor that accepts interface dependencies for better testability");
            }
            else
            {
                result.AddSuccess("Dependency injection constructor found");
            }

            // Check for static dependencies
            var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var method in methods)
            {
                var methodBody = method.GetMethodBody();
                // This is a simplified check - in a real implementation, you'd analyze IL code
                // or use more sophisticated analysis tools
                if (method.Name.Contains("DateTime.Now") || method.Name.Contains("File.") ||
                    method.Name.Contains("Environment."))
                {
                    result.AddIssue(TestabilityIssueLevel.Minor,
                        $"Potential static dependency in {method.Name}",
                        "Consider injecting system dependencies through interfaces");
                }
            }

            // Check for sealed class
            if (serviceType.IsSealed)
            {
                result.AddIssue(TestabilityIssueLevel.Minor,
                    "Class is sealed",
                    "Consider making non-sealed for easier mocking");
            }

            return result;
        }

        /// <summary>
        /// Creates a basic null logger for testing scenarios where logging is not important.
        /// </summary>
        /// <returns>Logger that discards all messages</returns>
        public static IQobuzLogger CreateNullLogger()
        {
            return new NullTestLogger();
        }

        /// <summary>
        /// Creates a memory logger that captures log messages for test assertions.
        /// </summary>
        /// <returns>Logger that stores messages in memory</returns>
        public static MemoryTestLogger CreateMemoryLogger()
        {
            return new MemoryTestLogger();
        }

        private static object GetMockInstance(ITestableServiceRegistry registry, Type serviceType)
        {
            // Use reflection to call generic GetService method
            var method = typeof(ITestableServiceRegistry)
                .GetMethod(nameof(ITestableServiceRegistry.GetService))
                .MakeGenericMethod(serviceType);

            return method.Invoke(registry, null);
        }
    }

    /// <summary>
    /// Result of testability validation with findings and recommendations.
    /// </summary>
    public class TestabilityValidationResult
    {
        public System.Collections.Generic.List<TestabilityIssue> Issues { get; } = new System.Collections.Generic.List<TestabilityIssue>();
        public System.Collections.Generic.List<string> Successes { get; } = new System.Collections.Generic.List<string>();

        public bool IsTestable => Issues.TrueForAll(i => i.Level != TestabilityIssueLevel.Critical);
        public int Score => Math.Max(0, 100 - Issues.Aggregate(0, (acc, issue) => acc + ((int)issue.Level * 10)));

        public void AddIssue(TestabilityIssueLevel level, string title, string recommendation)
        {
            Issues.Add(new TestabilityIssue(level, title, recommendation));
        }

        public void AddSuccess(string message)
        {
            Successes.Add(message);
        }
    }

    public class TestabilityIssue
    {
        public TestabilityIssueLevel Level { get; }
        public string Title { get; }
        public string Recommendation { get; }
        public DateTime DetectedAt { get; }

        public TestabilityIssue(TestabilityIssueLevel level, string title, string recommendation)
        {
            Level = level;
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Recommendation = recommendation ?? throw new ArgumentNullException(nameof(recommendation));
            DetectedAt = DateTime.UtcNow;
        }
    }

    public enum TestabilityIssueLevel
    {
        Minor = 1,
        Major = 2,
        Critical = 3
    }

    /// <summary>
    /// Null logger implementation for tests that don't need logging output.
    /// </summary>
    internal class NullTestLogger : IQobuzLogger
    {
        public void Debug(string message, params object[] args) { }
        public void Info(string message, params object[] args) { }
        public void Warn(string message, params object[] args) { }
        public void Warn(Exception ex, string message, params object[] args) { }
        public void Error(Exception ex, string message, params object[] args) { }
        public void Error(string message, params object[] args) { }
    }

    /// <summary>
    /// Memory logger that captures log messages for test verification.
    /// </summary>
    public class MemoryTestLogger : IQobuzLogger
    {
        public System.Collections.Generic.List<LogEntry> Entries { get; } = new System.Collections.Generic.List<LogEntry>();

        public void Debug(string message, params object[] args)
        {
            Entries.Add(new LogEntry(LogLevel.Debug, FormatMessage(message, args), null));
        }

        public void Info(string message, params object[] args)
        {
            Entries.Add(new LogEntry(LogLevel.Info, FormatMessage(message, args), null));
        }

        public void Warn(string message, params object[] args)
        {
            Entries.Add(new LogEntry(LogLevel.Warn, FormatMessage(message, args), null));
        }

        public void Warn(Exception ex, string message, params object[] args)
        {
            Entries.Add(new LogEntry(LogLevel.Warn, FormatMessage(message, args), ex));
        }

        public void Error(Exception ex, string message, params object[] args)
        {
            Entries.Add(new LogEntry(LogLevel.Error, FormatMessage(message, args), ex));
        }

        public void Error(string message, params object[] args)
        {
            Entries.Add(new LogEntry(LogLevel.Error, FormatMessage(message, args), null));
        }

        public bool HasLogsOfLevel(LogLevel level)
        {
            return Entries.Exists(e => e.Level == level);
        }

        public int GetLogCount(LogLevel level)
        {
            // Wave 82 polish: `.Count(predicate)` skips the `.ToList()` allocation
            // entirely. Same result, less GC pressure in test loops.
            return Entries.Count(e => e.Level == level);
        }

        public void Clear()
        {
            Entries.Clear();
        }

        private string FormatMessage(string message, params object[] args)
        {
            return args?.Length > 0 ? string.Format(message, args) : message;
        }
    }

    public class LogEntry
    {
        public LogLevel Level { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public DateTime Timestamp { get; }

        public LogEntry(LogLevel level, string message, Exception exception)
        {
            Level = level;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Exception = exception;
            Timestamp = DateTime.UtcNow;
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }
}
