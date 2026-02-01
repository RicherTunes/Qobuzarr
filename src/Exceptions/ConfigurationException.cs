using System;

namespace Lidarr.Plugin.Qobuzarr.Exceptions
{
    /// <summary>
    /// Exception thrown when there are configuration issues.
    /// </summary>
    public class ConfigurationException : Exception
    {
        /// <summary>
        /// The configuration key or setting that has an issue.
        /// </summary>
        public string ConfigurationKey { get; }

        /// <summary>
        /// The type of configuration issue.
        /// </summary>
        public ConfigurationIssueType IssueType { get; }

        public ConfigurationException(string message, string configurationKey, ConfigurationIssueType issueType)
            : base(message)
        {
            ConfigurationKey = configurationKey;
            IssueType = issueType;
        }

        public ConfigurationException(string message, string configurationKey, ConfigurationIssueType issueType, Exception innerException)
            : base(message, innerException)
        {
            ConfigurationKey = configurationKey;
            IssueType = issueType;
        }

        /// <summary>
        /// Creates an exception for missing required configuration.
        /// </summary>
        public static ConfigurationException MissingRequired(string configKey)
        {
            return new ConfigurationException(
                $"Required configuration '{configKey}' is missing or empty",
                configKey,
                ConfigurationIssueType.MissingRequired);
        }

        /// <summary>
        /// Creates an exception for invalid configuration value.
        /// </summary>
        public static ConfigurationException InvalidValue(string configKey, string value, string expectedFormat)
        {
            return new ConfigurationException(
                $"Configuration '{configKey}' has invalid value '{value}'. Expected format: {expectedFormat}",
                configKey,
                ConfigurationIssueType.InvalidValue);
        }

        /// <summary>
        /// Creates an exception for inaccessible paths.
        /// </summary>
        public static ConfigurationException PathNotAccessible(string configKey, string path, Exception innerException = null)
        {
            return new ConfigurationException(
                $"Path configured in '{configKey}' is not accessible: {path}",
                configKey,
                ConfigurationIssueType.PathNotAccessible,
                innerException);
        }

        /// <summary>
        /// Creates an exception for invalid combination of settings.
        /// </summary>
        public static ConfigurationException InvalidCombination(string message, params string[] configKeys)
        {
            return new ConfigurationException(
                message,
                string.Join(", ", configKeys),
                ConfigurationIssueType.InvalidCombination);
        }
    }

    /// <summary>
    /// Types of configuration issues.
    /// </summary>
    public enum ConfigurationIssueType
    {
        /// <summary>
        /// A required configuration value is missing.
        /// </summary>
        MissingRequired,

        /// <summary>
        /// A configuration value has an invalid format or value.
        /// </summary>
        InvalidValue,

        /// <summary>
        /// A configured path is not accessible.
        /// </summary>
        PathNotAccessible,

        /// <summary>
        /// Multiple configuration values have an invalid combination.
        /// </summary>
        InvalidCombination,

        /// <summary>
        /// Configuration value is out of acceptable range.
        /// </summary>
        OutOfRange
    }
}
